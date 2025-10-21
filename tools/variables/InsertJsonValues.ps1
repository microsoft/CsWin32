$vstsDropNames = & "$PSScriptRoot\VstsDropNames.ps1"
$BuildConfiguration = $env:BUILDCONFIGURATION
if (!$BuildConfiguration) {
    $BuildConfiguration = 'Debug'
}

$BasePath = "$PSScriptRoot\..\..\bin\Packages\$BuildConfiguration\Vsix"

if (Test-Path $BasePath) {
    $vsmanFiles = @()
    Get-ChildItem $BasePath *.vsman -Recurse -File | % {
        $version = (Get-Content $_.FullName | ConvertFrom-Json).info.buildVersion
        $fullPath = (Resolve-Path $_.FullName).Path
        $basePath = (Resolve-Path $BasePath).Path
        # Cannot use RelativePath or GetRelativePath due to Powershell Core v2.0 limitation
        if ($fullPath.StartsWith($basePath, [StringComparison]::OrdinalIgnoreCase)) {
            # Get the relative paths then make sure the directory separators match URL format.
            $rfn = $fullPath.Substring($basePath.Length).TrimStart('\', '/').Replace('\', '/')
        }
        else {
            $rfn = $fullPath  # fallback to full path if it doesn't start with base path
        }

        $fn = $_.Name

        # The left side is filename followed by the version and the right side is the drop url and the relative filename
        $thisVsManFile = "$fn{$version}=https://vsdrop.corp.microsoft.com/file/v1/$vstsDropNames;$rfn"
        $vsmanFiles += $thisVsManFile
    }

    [string]::join(',', $vsmanFiles)
}
