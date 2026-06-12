[CmdletBinding()]
Param(
)

$result = @{}

# Crash dumps and related diagnostics are staged into $(Build.ArtifactStagingDirectory)/crashDumps
# by tools/dotnet-test-cloud.ps1 when --blame-crash or the .NET DbgEnableMiniDump runtime
# environment captures a dump (e.g. for OOM-killed test hosts on Linux).
$artifactStaging = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$dumpsPath = Join-Path $artifactStaging 'crashDumps'
if (Test-Path $dumpsPath) {
    $files = @(Get-ChildItem $dumpsPath -Recurse -File)
    if ($files.Count -gt 0) {
        $result[$dumpsPath] = $files
    }
}

$result
