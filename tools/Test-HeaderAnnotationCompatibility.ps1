<#
.SYNOPSIS
Builds a pinned CsWin32 baseline and runs the canonical and optional full-image consumer matrices.
.DESCRIPTION
All git mutations and generated artifacts stay under this worktree. The caller source, requested
APIs, generation options and compilation context are shared between the three matrix legs.
No package feeds or dependency versions are changed.
An optional unmigrated tooling control audits physical constructor signatures separately,
without replacing OLD or standing in for NEW consumer acceptance.
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$BaselineCommit = 'f3f5a69aa4823466ef7b55bbdc8e5ba41ec021e6',
    [string]$OldMetadata,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$OldSHA256,
    [string]$NewMetadata,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$NewSHA256,
    [string]$ToolingControlMetadata,
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string]$ToolingControlSHA256
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$output = Join-Path $repo 'obj\HeaderAnnotationCompatibility'
$baseline = Join-Path $output "baseline-$BaselineCommit"
$baselineProject = Join-Path $baseline 'src\Microsoft.Windows.CsWin32\Microsoft.Windows.CsWin32.csproj'
$baselineAssembly = Join-Path $baseline 'bin\Microsoft.Windows.CsWin32\Debug\netstandard2.0\Microsoft.Windows.CsWin32.dll'
New-Item -ItemType Directory -Path $output -Force | Out-Null

if ($OldMetadata -or $NewMetadata -or $OldSHA256 -or $NewSHA256) {
    if (!$OldMetadata -or !$OldSHA256 -or ([bool]$NewMetadata -ne [bool]$NewSHA256)) {
        throw 'Supply the OLD metadata and hash, plus either both NEW metadata and hash or neither.'
    }
    $OldMetadata = (Resolve-Path $OldMetadata).Path
    if ((Get-FileHash $OldMetadata -Algorithm SHA256).Hash -ne $OldSHA256) {
        throw 'The OLD metadata snapshot does not match its pinned SHA256.'
    }
    if ($NewMetadata) {
        $NewMetadata = (Resolve-Path $NewMetadata).Path
        if ((Get-FileHash $NewMetadata -Algorithm SHA256).Hash -ne $NewSHA256) {
            throw 'The NEW metadata snapshot does not match its pinned SHA256.'
        }
    }
}

if ($ToolingControlMetadata -or $ToolingControlSHA256) {
    if (!$OldMetadata -or !$ToolingControlMetadata -or !$ToolingControlSHA256) {
        throw 'A tooling control requires the frozen OLD image and both control metadata and hash.'
    }
    $ToolingControlMetadata = (Resolve-Path $ToolingControlMetadata).Path
    if ((Get-FileHash $ToolingControlMetadata -Algorithm SHA256).Hash -ne $ToolingControlSHA256) {
        throw 'The unmigrated tooling control does not match its pinned SHA256.'
    }
}

if (!(Test-Path $baselineProject)) {
    $archive = Join-Path $output "$BaselineCommit.zip"
    & git -C $repo archive --format=zip "--output=$archive" $BaselineCommit
    if ($LASTEXITCODE) { throw 'Could not archive the pinned baseline commit.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $baseline
    Remove-Item -LiteralPath $archive
}

$priorEnvironment = @{}
$variables = @('NBGV_GitEngine', 'CSWIN32_COMPAT_BASELINE_DLL', 'CSWIN32_COMPAT_BASELINE_COMMIT',
    'CSWIN32_COMPAT_OLD_METADATA', 'CSWIN32_COMPAT_NEW_METADATA', 'CSWIN32_COMPAT_OLD_SHA256', 'CSWIN32_COMPAT_NEW_SHA256',
    'CSWIN32_COMPAT_TOOLING_CONTROL_METADATA', 'CSWIN32_COMPAT_TOOLING_CONTROL_SHA256', 'CSWIN32_COMPAT_OUTPUT', 'CSWIN32_COMPAT_PROPOSED_COMMIT')
foreach ($variable in $variables) {
    $priorEnvironment[$variable] = [Environment]::GetEnvironmentVariable($variable, 'Process')
}

try {
    $env:NBGV_GitEngine = 'Disabled'
    $buildLog = Join-Path $output 'baseline-build.log'
    & dotnet build $baselineProject --no-restore --verbosity quiet *> $buildLog
    if ($LASTEXITCODE -and (Select-String -LiteralPath $buildLog -Pattern 'NETSDK1004' -Quiet)) {
        & dotnet restore $baselineProject --verbosity quiet
        if ($LASTEXITCODE) { throw 'Baseline restore failed.' }
        & dotnet build $baselineProject --no-restore --verbosity quiet *> $buildLog
    }
    if ($LASTEXITCODE) {
        Get-Content -LiteralPath $buildLog
        throw 'Baseline build failed.'
    }
    $env:CSWIN32_COMPAT_BASELINE_DLL = $baselineAssembly
    $env:CSWIN32_COMPAT_BASELINE_COMMIT = $BaselineCommit
    $env:CSWIN32_COMPAT_PROPOSED_COMMIT = & git -C $repo rev-parse HEAD
    if ($LASTEXITCODE) { throw 'Could not resolve the proposed commit.' }
    if (& git -C $repo status --porcelain) {
        $env:CSWIN32_COMPAT_PROPOSED_COMMIT += '+working-tree'
    }
    $env:CSWIN32_COMPAT_OUTPUT = $output
    $env:CSWIN32_COMPAT_OLD_METADATA = $OldMetadata
    $env:CSWIN32_COMPAT_NEW_METADATA = $NewMetadata
    $env:CSWIN32_COMPAT_OLD_SHA256 = $OldSHA256.ToUpperInvariant()
    $env:CSWIN32_COMPAT_NEW_SHA256 = $NewSHA256.ToUpperInvariant()
    $env:CSWIN32_COMPAT_TOOLING_CONTROL_METADATA = $ToolingControlMetadata
    $env:CSWIN32_COMPAT_TOOLING_CONTROL_SHA256 = $ToolingControlSHA256.ToUpperInvariant()
    & dotnet test (Join-Path $repo 'test\Microsoft.Windows.CsWin32.Tests\Microsoft.Windows.CsWin32.Tests.csproj') --no-restore `
        --filter 'FullyQualifiedName~HeaderAnnotationCompatibilityTests|FullyQualifiedName~AssociatedEnumTests.CallerMutationControls' --logger 'trx;LogFileName=compatibility.trx' --results-directory $output --verbosity quiet
    if ($LASTEXITCODE) { throw "Compatibility checks failed. See $output." }
    Write-Output "Compatibility reports and generated sources: $output"
}
finally {
    foreach ($variable in $variables) {
        [Environment]::SetEnvironmentVariable($variable, $priorEnvironment[$variable], 'Process')
    }
}
