Function Get-FileFromWeb([Uri]$Uri, $OutFile) {
    $OutDir = Split-Path $OutFile
    if (!(Test-Path $OutFile)) {
        Write-Verbose "Downloading $Uri..."
        if (!(Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
        try {
            (New-Object System.Net.WebClient).DownloadFile($Uri, $OutFile)
        }
        finally {
            # This try/finally causes the script to abort
        }
    }
}

Function Unzip($Path, $OutDir) {
    $OutDir = (New-Item -ItemType Directory -Path $OutDir -Force).FullName
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    # Start by extracting to a temporary directory so that there are no file conflicts.
    [System.IO.Compression.ZipFile]::ExtractToDirectory($Path, "$OutDir.out")

    # Now move all files from the temp directory to $OutDir, overwriting any files.
    Get-ChildItem -Path "$OutDir.out" -Recurse -File | ForEach-Object {
        $destinationPath = Join-Path -Path $OutDir -ChildPath $_.FullName.Substring("$OutDir.out".Length).TrimStart([io.path]::DirectorySeparatorChar, [io.path]::AltDirectorySeparatorChar)
        if (!(Test-Path -Path (Split-Path -Path $destinationPath -Parent))) {
            New-Item -ItemType Directory -Path (Split-Path -Path $destinationPath -Parent) | Out-Null
        }
        Move-Item -Path $_.FullName -Destination $destinationPath -Force
    }
    Remove-Item -Path "$OutDir.out" -Recurse -Force
}

Function Get-SymbolsFromPackage($id, $version) {
    $symbolPackagesPath = "$PSScriptRoot/../obj/SymbolsPackages"
    New-Item -ItemType Directory -Path $symbolPackagesPath -Force | Out-Null
    $nupkgPath = Join-Path $symbolPackagesPath "$id.$version.nupkg"
    $snupkgPath = Join-Path $symbolPackagesPath "$id.$version.snupkg"
    $unzippedPkgPath = Join-Path $symbolPackagesPath "$id.$version"
    Get-FileFromWeb -Uri "https://www.nuget.org/api/v2/package/$id/$version" -OutFile $nupkgPath
    Get-FileFromWeb -Uri "https://www.nuget.org/api/v2/symbolpackage/$id/$version" -OutFile $snupkgPath

    Unzip -Path $nupkgPath -OutDir $unzippedPkgPath
    Unzip -Path $snupkgPath -OutDir $unzippedPkgPath

    Get-ChildItem -Recurse -LiteralPath $unzippedPkgPath -Filter *.pdb | % {
        # Collect the DLLs/EXEs as well.
        $rootName = Join-Path $_.Directory $_.BaseName
        if ($rootName.EndsWith('.ni')) {
            $rootName = $rootName.Substring(0, $rootName.Length - 3)
        }

        $dllPath = "$rootName.dll"
        $exePath = "$rootName.exe"
        if (Test-Path $dllPath) {
            $BinaryImagePath = $dllPath
        }
        elseif (Test-Path $exePath) {
            $BinaryImagePath = $exePath
        }
        else {
            Write-Warning "`"$_`" found with no matching binary file."
            $BinaryImagePath = $null
        }

        if ($BinaryImagePath) {
            Write-Output $BinaryImagePath
            Write-Output $_.FullName
        }
    }
}

Function Get-PackageVersion($id) {
    $versionProps = [xml](Get-Content -LiteralPath $PSScriptRoot\..\Directory.Packages.props)
    $version = $versionProps.Project.ItemGroup.PackageVersion | ? { $_.Include -eq $id } | % { $_.Version }
    if (!$version) {
        Write-Error "No package version found in Directory.Packages.props for the package '$id'"
    }

    $version
}

# All 3rd party packages for which symbols packages are expected should be listed here.
# These must all be sourced from nuget.org, as it is the only feed that supports symbol packages.
$3rdPartyPackageIds = @()

$3rdPartyPackageIds | % {
    $version = Get-PackageVersion $_
    if ($version) {
        Get-SymbolsFromPackage -id $_ -version $version
    }
}
