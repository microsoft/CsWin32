$RepoRoot = [System.IO.Path]::GetFullPath("$PSScriptRoot\..\..")

$coverageFilesUnderRoot = @(Get-ChildItem "$RepoRoot/test/*.cobertura.xml" -Recurse | Where-Object { $_.FullName -notlike "*/In/*" -and $_.FullName -notlike "*\In\*" })

# MTP writes coverage files directly to the requested results directory.
$ArtifactStagingFolder = & "$PSScriptRoot/../Get-ArtifactsStagingDirectory.ps1"
$directTestLogs = Join-Path $ArtifactStagingFolder test_logs
$coverageFilesUnderArtifacts = if (Test-Path $directTestLogs) { @(Get-ChildItem "$directTestLogs/*.cobertura.xml" -Recurse) } else { @() }

# Prepare code coverage reports for merging on another machine
$repoRoot = $env:SYSTEM_DEFAULTWORKINGDIRECTORY
if (!$repoRoot) { $repoRoot = $env:GITHUB_WORKSPACE }
if ($repoRoot) {
    Write-Host "Substituting $repoRoot with `"{reporoot}`""
    @($coverageFilesUnderRoot + $coverageFilesUnderArtifacts) | Where-Object { $_ } | ForEach-Object {
        $content = Get-Content -LiteralPath $_ |% { $_ -Replace [regex]::Escape($repoRoot), "{reporoot}" }
        Set-Content -LiteralPath $_ -Value $content -Encoding UTF8
    }
} else {
    Write-Warning "coverageResults: Cloud build not detected. Machine-neutral token replacement skipped."
}

if (!((Test-Path $RepoRoot\bin) -and (Test-Path $RepoRoot\obj))) { return }

@{
    $directTestLogs = $coverageFilesUnderArtifacts;
    $RepoRoot = (
        $coverageFilesUnderRoot +
        (Get-ChildItem "$RepoRoot\obj\*.cs" -Recurse)
    );
}
