$BinPath = [System.IO.Path]::GetFullPath("$PSScriptRoot/../../bin")
$3rdPartyPath = [System.IO.Path]::GetFullPath("$PSScriptRoot/../../obj/SymbolsPackages")
if (!(Test-Path $BinPath)) { return }
$symbolfiles = & "$PSScriptRoot/../Get-SymbolFiles.ps1" -Path $BinPath | Get-Unique
$3rdPartyFiles = & "$PSScriptRoot/../Get-3rdPartySymbolFiles.ps1"

@{
    "$BinPath" = $SymbolFiles;
    "$3rdPartyPath" = $3rdPartyFiles;
}
