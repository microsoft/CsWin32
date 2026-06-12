#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs tests as they are run in cloud test runs.
.PARAMETER Configuration
    The configuration within which to run tests
.PARAMETER Agent
    The name of the agent. This is used in preparing test run titles.
.PARAMETER PublishResults
    A switch to publish results to Azure Pipelines.
.PARAMETER x86
    A switch to run the tests in an x86 process.
.PARAMETER dotnet32
    The path to a 32-bit dotnet executable to use.
#>
[CmdletBinding()]
Param(
    [string]$Configuration='Debug',
    [string]$Agent='Local',
    [switch]$PublishResults,
    [switch]$x86,
    [string]$dotnet32
)

$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path
$ArtifactStagingFolder = & "$PSScriptRoot/Get-ArtifactsStagingDirectory.ps1"

$dotnet = 'dotnet'
if ($x86) {
  $x86RunTitleSuffix = ", x86"
  if ($dotnet32) {
    $dotnet = $dotnet32
  } else {
    $dotnet32Possibilities = "$PSScriptRoot\../obj/tools/x86/.dotnet/dotnet.exe", "$env:AGENT_TOOLSDIRECTORY/x86/dotnet/dotnet.exe", "${env:ProgramFiles(x86)}\dotnet\dotnet.exe"
    $dotnet32Matches = $dotnet32Possibilities |? { Test-Path $_ }
    if ($dotnet32Matches) {
      $dotnet = Resolve-Path @($dotnet32Matches)[0]
      Write-Host "Running tests using `"$dotnet`"" -ForegroundColor DarkGray
    } else {
      Write-Error "Unable to find 32-bit dotnet.exe"
      return 1
    }
  }
}

$testBinLog = Join-Path $ArtifactStagingFolder (Join-Path build_logs test.binlog)
$testDiagLog = Join-Path $ArtifactStagingFolder (Join-Path test_logs diag.log)
$dumpStagingFolder = Join-Path $ArtifactStagingFolder 'crashDumps'

function Invoke-DotnetTest {
    param(
        [Parameter(Mandatory)][string]$Target,
        [string]$BinLogSuffix = ''
    )

    $binLogPath = if ($BinLogSuffix) {
        Join-Path $ArtifactStagingFolder (Join-Path build_logs "test.$BinLogSuffix.binlog")
    } else {
        $testBinLog
    }

    & $dotnet test $Target `
        --no-build `
        -c $Configuration `
        --filter "TestCategory!=HighMemory&TestCategory!=RequiresHardware$env:TESTFILTER" `
        --collect "Code Coverage;Format=cobertura" `
        --settings "$PSScriptRoot/test.runsettings" `
        --blame-hang-timeout 1500s `
        --blame-crash `
        -bl:"$binLogPath" `
        --diag "$testDiagLog;TraceLevel=info" `
        --logger trx
}

function Write-MemorySnapshot([string]$Label) {
    if (-not $IsLinux) { return }
    Write-Host ""
    Write-Host "==== $Label ====" -ForegroundColor Yellow
    & free -h 2>&1 | Out-Host
    Write-Host "---- /proc/meminfo (top 10) ----"
    Get-Content /proc/meminfo -TotalCount 10 -ErrorAction SilentlyContinue | Out-Host
}

function Write-DmesgTail {
    if (-not $IsLinux) { return }
    Write-Host ""
    Write-Host "==== dmesg tail (looking for OOM killer messages) ====" -ForegroundColor Yellow
    # dmesg requires CAP_SYSLOG; try direct first, then sudo (most ADO Linux agents have passwordless sudo).
    $out = & dmesg --ctime 2>$null | Select-Object -Last 200
    if ($LASTEXITCODE -ne 0 -or -not $out) {
        $out = & sudo -n dmesg --ctime 2>$null | Select-Object -Last 200
    }
    if ($LASTEXITCODE -ne 0 -or -not $out) {
        Write-Host '(dmesg unavailable: kernel.dmesg_restrict=1 and sudo not permitted)' -ForegroundColor DarkGray
    } else {
        $out | Out-Host
    }
}

# Enable .NET runtime crash dumps on managed unhandled exceptions / aborts.
# These are emitted in addition to the dumps captured by `--blame-crash`,
# and survive scenarios where blame's createdump invocation cannot fire
# (for example, SIGKILL by the kernel OOM-killer never reaches managed code,
# but a runtime abort / unhandled exception that precedes the kill is captured).
New-Item -ItemType Directory -Force -Path $dumpStagingFolder | Out-Null
# Always drop a readme so the artifact upload has at least one file to publish.
@'
This artifact collects test-host crash dumps captured by `dotnet test --blame-crash`
and by the .NET runtime (DOTNET_DbgEnableMiniDump).

If the artifact only contains this README, no managed crashes were captured. That is
often the case when a test host is killed by the kernel (e.g. the Linux OOM-killer
sends SIGKILL), since SIGKILL gives the runtime no opportunity to write a dump.
In that case, inspect the test step's console output for memory diagnostics and
the `dmesg` tail that the test script captures after a failure.
'@ | Set-Content -Path (Join-Path $dumpStagingFolder 'README.txt')
$env:DOTNET_DbgEnableMiniDump = '1'
$env:DOTNET_DbgMiniDumpType = '2' # 2 = Heap (managed heap + threads; smaller than full memory)
$env:DOTNET_DbgMiniDumpName = (Join-Path $dumpStagingFolder 'coredump.%p.%t.dmp')
$env:DOTNET_CreateDumpDiagnostics = '1'

$overallExitCode = 0

if (-not $IsWindows -and -not $x86) {
    # On Linux/macOS the generator test projects each consume several GB of RAM.
    # `dotnet test <slnFile>` runs vstest hosts for multiple test projects in parallel,
    # and on memory-constrained agents that causes the kernel OOM-killer to terminate
    # one of the hosts (exit code 137 = SIGKILL). The previous attempt to fix this
    # via XUNIT_MAX_PARALLEL_THREADS / BuildInParallel / RunConfiguration.MaxCpuCount
    # only limits parallelism *within* a single test assembly, not across them.
    #
    # Mirror the working GitHub Actions Linux workflow: enumerate test projects and
    # invoke `dotnet test` once per project, serially.
    Write-Host 'Non-Windows agent: serializing test runs project-by-project to avoid OOM kills.' -ForegroundColor Cyan
    Write-MemorySnapshot 'Pre-test memory state'

    $testProjects = Get-ChildItem -Path "$RepoRoot/test" -Directory |
        Where-Object Name -Like '*.Tests' |
        Sort-Object Name |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.csproj' | Select-Object -First 1 } |
        Where-Object { $_ }

    foreach ($proj in $testProjects) {
        $projName = $proj.BaseName
        Write-Host ''
        Write-Host "▶️  Running tests in $projName" -ForegroundColor Cyan
        Write-MemorySnapshot "Memory before $projName"
        Invoke-DotnetTest -Target $proj.FullName -BinLogSuffix $projName
        $thisExit = $LASTEXITCODE
        if ($thisExit -ne 0) {
            Write-Host "❌ Tests in $projName exited with code $thisExit" -ForegroundColor Red
            $overallExitCode = $thisExit
            Write-MemorySnapshot "Post-failure memory state ($projName)"
            Write-DmesgTail
        }
    }
} else {
    Invoke-DotnetTest -Target $RepoRoot
    $overallExitCode = $LASTEXITCODE
}

# Move any captured crash dumps (from --blame-crash or DOTNET_DbgEnableMiniDump) into
# the dedicated staging folder so they're easy to find in the published artifact.
Get-ChildItem -Path "$RepoRoot/test" -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like '*.dmp' -or $_.Name -like 'core.*' -or $_.Name -like 'coredump.*' } |
    ForEach-Object {
        $dest = Join-Path $dumpStagingFolder $_.Name
        try {
            Move-Item -Path $_.FullName -Destination $dest -Force -ErrorAction Stop
            Write-Host "Collected crash dump: $($_.Name) ($([math]::Round($_.Length / 1MB, 1)) MB)"
        } catch {
            Write-Host "Failed to move crash dump $($_.FullName): $_" -ForegroundColor Yellow
        }
    }

$unknownCounter = 0
Get-ChildItem -Recurse -Path $RepoRoot\test\*.trx |% {
  Copy-Item $_ -Destination $ArtifactStagingFolder/test_logs/

  if ($PublishResults) {
    $x = [xml](Get-Content -LiteralPath $_)
    $runTitle = $null
    if ($x.TestRun.TestDefinitions -and $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')) {
      $storage = $x.TestRun.TestDefinitions.GetElementsByTagName('UnitTest')[0].storage -replace '\\','/'
      if ($storage -match '/(?<tfm>net[^/]+)/(?:(?<rid>[^/]+)/)?(?<lib>[^/]+)\.(dll|exe)$') {
        if ($matches.rid) {
          $runTitle = "$($matches.lib) ($($matches.tfm), $($matches.rid), $Agent)"
        } else {
          $runTitle = "$($matches.lib) ($($matches.tfm)$x86RunTitleSuffix, $Agent)"
        }
      }
    }
    if (!$runTitle) {
      $unknownCounter += 1;
      $runTitle = "unknown$unknownCounter ($Agent$x86RunTitleSuffix)";
    }

    Write-Host "##vso[results.publish type=VSTest;runTitle=$runTitle;publishRunAttachments=true;resultFiles=$_;failTaskOnFailedTests=true;testRunSystem=VSTS - PTR;]"
  }
}

exit $overallExitCode
