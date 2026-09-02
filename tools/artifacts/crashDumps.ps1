[CmdletBinding()]
Param(
)

$result = @{}

# Crash dumps and related diagnostics are staged into $(Build.ArtifactStagingDirectory)/crashDumps
# by tools/dotnet-test-cloud.ps1 when the MTP crash/hang dump extensions or the .NET
# DbgEnableMiniDump runtime environment captures a dump.
$artifactStaging = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$dumpsPath = Join-Path $artifactStaging 'crashDumps'
if (Test-Path $dumpsPath) {
    $files = @(Get-ChildItem $dumpsPath -Recurse -File)
    if ($files.Count -gt 0) {
        $result[$dumpsPath] = $files
    }
}

$result
