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

function Write-MemorySnapshot([string]$Label) {
    if (-not $IsLinux) { return }
    Write-Host ""
    Write-Host "==== $Label ====" -ForegroundColor Yellow
    & free -h 2>&1 | Out-Host
    Write-Host "---- /proc/meminfo (top 12) ----"
    Get-Content /proc/meminfo -TotalCount 12 -ErrorAction SilentlyContinue | Out-Host
}

function Write-DmesgTail {
    if (-not $IsLinux) { return }
    Write-Host ""
    Write-Host "==== dmesg tail (looking for OOM killer messages) ====" -ForegroundColor Yellow
    # dmesg requires CAP_SYSLOG on most agents; try sudo (passwordless on ADO Linux pools).
    # Filter to entries that mention oom/kill/Killed so the log stays compact.
    $cmd = "(sudo -n dmesg --ctime 2>/dev/null || dmesg --ctime 2>/dev/null) | tail -n 400"
    $out = & bash -c $cmd 2>$null
    if (-not $out) {
        Write-Host '(dmesg unavailable: kernel.dmesg_restrict=1 and sudo not permitted)' -ForegroundColor DarkGray
        return
    }
    # Surface OOM-relevant lines first, then a compact tail of everything else.
    $oomLines = $out | Select-String -Pattern '(oom|killed|Killed|invoked oom-killer|Out of memory|memory cgroup)' -CaseSensitive:$false
    if ($oomLines) {
        Write-Host "---- OOM-related entries ----" -ForegroundColor Red
        $oomLines | ForEach-Object { Write-Host $_.Line }
    } else {
        Write-Host "(no OOM-killer entries detected in dmesg tail)" -ForegroundColor DarkGray
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

# On Linux/macOS, the heavy generator test projects each consume several GB of RAM,
# and the default `dotnet test <slnFile>` schedules MSBuild's VSTest target for multiple
# projects in parallel — causing the kernel OOM-killer to terminate test hosts on the
# memory-constrained ADO agents (exit code 137 = SIGKILL). Force a single MSBuild node
# so VSTest is invoked one project at a time. Use the solution-level invocation so that
# the sln's `NonWindows` configuration correctly filters out Windows-only projects.
$extraTestArgs = @()
if (-not $IsWindows -and -not $x86) {
    Write-Host 'Non-Windows agent: forcing single MSBuild node (-m:1) to serialize test runs.' -ForegroundColor Cyan
    $extraTestArgs += '-m:1'
    # Also restrain xunit's intra-assembly parallelism: while it does not prevent
    # cross-project OOM on its own, it reduces peak RSS during the heavy test runs.
    $env:XUNIT_MAX_PARALLEL_THREADS = '1'
    Write-MemorySnapshot 'Pre-test memory state'
}

& $dotnet test $RepoRoot `
    --no-build `
    -c $Configuration `
    --filter "TestCategory!=HighMemory&TestCategory!=RequiresHardware$env:TESTFILTER" `
    --collect "Code Coverage;Format=cobertura" `
    --settings "$PSScriptRoot/test.runsettings" `
    --blame-hang-timeout 1500s `
    --blame-crash `
    -bl:"$testBinLog" `
    --diag "$testDiagLog;TraceLevel=info" `
    --logger trx `
    @extraTestArgs

$overallExitCode = $LASTEXITCODE
if ($overallExitCode -ne 0) {
    Write-Host "❌ dotnet test exited with code $overallExitCode" -ForegroundColor Red
    Write-MemorySnapshot 'Post-failure memory state'
    Write-DmesgTail
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
