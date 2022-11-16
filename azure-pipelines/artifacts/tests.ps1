$RepoRoot = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\..")
$BuildConfiguration = $env:BUILDCONFIGURATION
if (!$BuildConfiguration) {
    $BuildConfiguration = 'Debug'
}

$TestBinRoot = "$RepoRoot/bin/test"

if (!(Test-Path $TestBinRoot))  { return }

@{
    "$TestBinRoot" = (Get-ChildItem $TestBinRoot -Recurse)
}
