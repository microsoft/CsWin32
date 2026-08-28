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
$testLogs = Join-Path $ArtifactStagingFolder 'test_logs'
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

# Avoid publishing stale local artifacts from an earlier invocation.
if (Test-Path $testLogs) { Remove-Item $testLogs -Recurse -Force }
if (Test-Path $dumpStagingFolder) { Remove-Item $dumpStagingFolder -Recurse -Force }

# Enable .NET runtime crash dumps on managed unhandled exceptions / aborts.
# These complement the MTP crash dump extension and survive scenarios where
# the extension cannot fire before process termination.
New-Item -ItemType Directory -Force -Path $testLogs, $dumpStagingFolder, (Split-Path $testBinLog -Parent) | Out-Null
# Always drop a readme so the artifact upload has at least one file to publish.
@'
This artifact collects test-host crash and hang dumps captured by Microsoft Testing
Platform extensions and by the .NET runtime (DOTNET_DbgEnableMiniDump).

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

# On Linux/macOS, the heavy generator test projects each consume several GB of RAM.
# Serialize MTP test modules and restrain xUnit's intra-assembly parallelism to avoid
# the kernel OOM-killer on memory-constrained agents.
$extraTestArgs = @()
if (-not $IsWindows -and -not $x86) {
    Write-Host 'Non-Windows agent: serializing MTP test modules.' -ForegroundColor Cyan
    $extraTestArgs += '-p:Platform=NonWindows', '--max-parallel-test-modules', '1'
    $env:XUNIT_MAX_PARALLEL_THREADS = '1'
    Write-MemorySnapshot 'Pre-test memory state'
}

$filterQuery = "/[(TestCategory!=HighMemory)&(TestCategory!=RequiresHardware)$env:TESTFILTER]"
$solutionPath = Join-Path $RepoRoot 'Microsoft.Windows.CsWin32.sln'
$testArgs = @(
    '--solution', $solutionPath,
    '--no-build',
    '-c', $Configuration,
    "-bl:$testBinLog",
    '--filter-query', $filterQuery,
    '--coverage',
    '--coverage-output-format', 'cobertura',
    '--coverage-settings', "$PSScriptRoot/test.runsettings",
    '--hangdump',
    '--hangdump-timeout', '1500s',
    '--hangdump-type', 'Heap',
    '--crashdump',
    '--crashdump-type', 'Heap',
    '--diagnostic',
    '--diagnostic-output-directory', $testLogs,
    '--diagnostic-verbosity', 'Information',
    '--report-trx',
    '--results-directory', $testLogs
) + $extraTestArgs

& $dotnet test @testArgs

$overallExitCode = $LASTEXITCODE
if ($overallExitCode -ne 0) {
    Write-Host "❌ dotnet test exited with code $overallExitCode" -ForegroundColor Red
    Write-MemorySnapshot 'Post-failure memory state'
    Write-DmesgTail
}

# Move any captured crash or hang dumps into the dedicated staging folder.
@("$RepoRoot/test", $testLogs) |
  Where-Object { Test-Path $_ } |
  ForEach-Object { Get-ChildItem -Path $_ -Recurse -File -ErrorAction SilentlyContinue } |
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
Get-ChildItem -Recurse -Path $testLogs\*.trx | ForEach-Object {
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
      if ($_.BaseName -match '^(?<lib>.+)_(?<tfm>net[^_]+)_(?<arch>[^_]+)$') {
        $runTitle = "$($matches.lib) ($($matches.tfm), $($matches.arch), $Agent)"
      } else {
        $unknownCounter += 1;
        $runTitle = "unknown$unknownCounter ($Agent$x86RunTitleSuffix)";
      }
    }

    # Azure Pipelines uses "VSTest" as the TRX publication protocol identifier,
    # including when Microsoft Testing Platform produced the TRX file.
    Write-Host "##vso[results.publish type=VSTest;runTitle=$runTitle;publishRunAttachments=true;resultFiles=$_;failTaskOnFailedTests=true;testRunSystem=VSTS - PTR;]"
  }
}

exit $overallExitCode
