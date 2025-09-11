#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Runs the apicompat tool on the built packages.
.PARAMETER Configuration
    Debug or Release
.PARAMETER GenerateSuppressionFile
    If specified, generates or updates a suppression file for API compatibility checks.
#>
[CmdletBinding()]
Param (
    [string]$Configuration = 'Release',
    [switch]$GenerateSuppressionFile
)

Function Get-PackageId($nupkgPath) {
    # The package ID cannot be reliably parsed out of the nupkg file name,
    # because if the ID ends with an integer, it may be ambiguous with the version part.
    # Extract the nuspec file from the nupkg to get the package ID.
    $tempDir = [System.IO.Path]::GetTempPath() + [System.Guid]::NewGuid().ToString()
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    try {
        Expand-Archive -Path $nupkgPath -DestinationPath $tempDir
        $nuspecPath = Get-ChildItem -Path $tempDir -Filter "*.nuspec" | Select-Object -First 1
        if ($nuspecPath) {
            [xml]$nuspec = Get-Content $nuspecPath.FullName
            return $nuspec.package.metadata.id
        }
        else {
            Write-Warning "No nuspec found in $nupkgPath"
            return $null
        }
    }
    finally {
        Remove-Item -Recurse -Force $tempDir
    }
}

$failures = 0
Get-ChildItem $PSScriptRoot/../bin/Packages/$Configuration/*.nupkg | % {
    $packageId = Get-PackageId $_.FullName

    if ($packageId -eq $null) {
        continue
    }

    $compatArgs = 'apicompat', 'package', $_.FullName, '--enable-strict-mode-for-baseline-validation'

    # Attempt to download the last published stable version of this package
    # to serve as a baseline on which this new build should be API compatible.
    try {
        New-Item -ItemType Directory -Path "$PSScriptRoot/../obj" -Force | Out-Null
        $baselineFile = [System.IO.Path]::GetFullPath("$PSScriptRoot/../obj/$packageId.baseline.nupkg")
        Invoke-WebRequest -Uri https://www.nuget.org/api/v2/package/$packageId -OutFile $baselineFile
        $compatArgs += '--baseline-package', $baselineFile
    }
    catch {
        Write-Warning "Failed to download baseline package for $packageId, which may be expected if a stable package has not been published. $_"
    }

    $ApiCompatSuppressionPath = [System.IO.Path]::GetFullPath("$PSScriptRoot/../src/$packageId/ApiCompatSuppressions.xml")
    if ((Test-Path $ApiCompatSuppressionPath) -or $GenerateSuppressionFile) {
        $compatArgs += '--suppression-file', $ApiCompatSuppressionPath
    }
    if ($GenerateSuppressionFile) {
        $compatArgs += '--generate-suppression-file'
    }
    Write-Host "Testing $($packageId): dotnet $compatArgs"
    dotnet @compatArgs
    if ($LASTEXITCODE -ne 0) { $failures += 1 }
}

exit $failures
