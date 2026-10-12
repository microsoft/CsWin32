#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Exercises init.ps1 restore retries without installing prerequisites or restoring packages.
#>
[CmdletBinding()]
Param()

$ErrorActionPreference = 'Stop'
$initPath = (Resolve-Path (Join-Path $PSScriptRoot '..\init.ps1')).Path
$tokens = $null
$parseErrors = $null
$initAst = [System.Management.Automation.Language.Parser]::ParseFile($initPath, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) {
    throw ($parseErrors -join "`n")
}

# Load the production helper without executing init.ps1's installation and restore steps.
$retryFunction = $initAst.Find({
    Param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Invoke-RestoreWithRetry'
}, $false)
if ($null -eq $retryFunction) {
    throw 'Could not find Invoke-RestoreWithRetry in init.ps1.'
}
. ([scriptblock]::Create($retryFunction.Extent.Text))
$pwsh = (Get-Process -Id $PID).Path

<#
.SYNOPSIS
    Runs a native failing or succeeding restore command and checks attempts, warnings, and errors.
#>
function Test-RestoreRetry {
    Param(
        [string]$Name,
        [string]$Output = '',
        [int]$Failures = 0,
        [int]$RestoreRetryCount = 3,
        [int]$ExpectedAttempts = 1,
        [int]$ExpectedWarnings = 0,
        [switch]$StandardError,
        [switch]$ExpectFailure
    )

    $RestoreRetryDelaySeconds = 0
    $state = [pscustomobject]@{ Attempts = 0 }
    $warnings = @()
    $failure = $null
    try {
        Invoke-RestoreWithRetry -Restore {
            $PSNativeCommandUseErrorActionPreference = $false
            $state.Attempts++
            $exitCode = [int]($state.Attempts -le $Failures)
            $stream = if ($StandardError) { 'Error' } else { 'Out' }
            $command = "[Console]::$stream.WriteLine('$($Output.Replace("'", "''"))'); exit $exitCode"
            $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            & $pwsh -NoLogo -NoProfile -NonInteractive -EncodedCommand $encodedCommand
        } -FailureMessage 'Restore regression failure.' -WarningVariable warnings -WarningAction SilentlyContinue
    }
    catch {
        $failure = $_.Exception.Message
    }

    if ($state.Attempts -ne $ExpectedAttempts -or @($warnings).Count -ne $ExpectedWarnings) {
        throw "${Name}: expected $ExpectedAttempts attempts and $ExpectedWarnings warnings; got $($state.Attempts) attempts and $(@($warnings).Count) warnings."
    }
    if ($ExpectFailure) {
        if ($failure -ne 'Restore regression failure.') {
            throw "${Name}: expected restore failure; got '$failure'."
        }
    } elseif ($null -ne $failure) {
        throw "${Name}: unexpected failure '$failure'."
    }

    Write-Host "PASS: $Name"
}

$cacheMiss = "Response status code does not indicate success: 401 (Unauthorized - No local versions of package 'sample'; please provide authentication to access versions from upstream that have not yet been saved to your feed.)"
$toolFailure = 'Downloading sample-tool version 1.2.3 failed'
Test-RestoreRetry -Name 'Success does not retry'
Test-RestoreRetry -Name 'A successful exit takes precedence over output' -Output $cacheMiss
Test-RestoreRetry -Name 'Package cache miss retries until success' -Output $cacheMiss -Failures 2 -ExpectedAttempts 3 -ExpectedWarnings 2
Test-RestoreRetry -Name 'Cache miss on stderr retries until success' -Output $cacheMiss -StandardError -Failures 2 -ExpectedAttempts 3 -ExpectedWarnings 2
Test-RestoreRetry -Name 'Detailed tool cache miss retries until success' -Output "$toolFailure`n$cacheMiss" -Failures 2 -ExpectedAttempts 3 -ExpectedWarnings 2
Test-RestoreRetry -Name 'Retry exhaustion fails' -Output $cacheMiss -Failures 10 -RestoreRetryCount 10 -ExpectedAttempts 10 -ExpectedWarnings 9 -ExpectFailure
Test-RestoreRetry -Name 'Single-attempt caller never retries' -Output $cacheMiss -Failures 3 -RestoreRetryCount 1 -ExpectFailure
Test-RestoreRetry -Name 'Generic tool failure is not a cache miss' -Output $toolFailure -Failures 3 -ExpectFailure
Test-RestoreRetry -Name 'Tool authentication failure fails immediately' -Output "$toolFailure`nResponse status code does not indicate success: 401 (Unauthorized)." -Failures 3 -ExpectFailure
Test-RestoreRetry -Name 'Tool permission failure fails immediately' -Output "$toolFailure`nResponse status code does not indicate success: 403 (Forbidden)." -Failures 3 -ExpectFailure
Test-RestoreRetry -Name 'Missing package version fails immediately' -Output 'error NU1102: Unable to find package sample-tool with version 1.2.3' -Failures 3 -ExpectFailure
Test-RestoreRetry -Name 'Incompatible package fails immediately' -Output "$toolFailure`nerror NU1202: Package sample-tool is not compatible with net10.0." -Failures 3 -ExpectFailure
Test-RestoreRetry -Name 'Failure without output fails immediately' -Failures 3 -ExpectFailure

<#
.SYNOPSIS
    Checks the actual init.ps1 tool-restore arguments while replacing dotnet with a successful stub.
#>
function Test-ToolRestoreArguments {
    Param(
        [int]$RestoreRetryCount = 1,
        [switch]$Interactive
    )

    $state = [pscustomobject]@{ Arguments = @(); Calls = 0 }
    $originalDotnet = Get-Item Function:global:dotnet -ErrorAction SilentlyContinue
    try {
        Set-Item Function:global:dotnet -Value {
            $state.Arguments = @($args)
            $state.Calls++
            $global:LASTEXITCODE = 0
        }.GetNewClosure()
        $initArguments = @{ NoPrerequisites = $true; NoRestore = $true; Interactive = $Interactive }
        if ($PSBoundParameters.ContainsKey('RestoreRetryCount')) {
            $initArguments.RestoreRetryCount = $RestoreRetryCount
        }
        & $initPath @initArguments

        $retryCountParameter = $initAst.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -eq 'RestoreRetryCount' }
        if ($retryCountParameter.DefaultValue.Value -ne 1) {
            throw 'The default retry count must remain one.'
        }
        $expectedArguments = @('tool', 'restore')
        if ($Interactive) {
            $expectedArguments += '--interactive'
        }
        if ($RestoreRetryCount -gt 1) {
            $expectedArguments += '--verbosity', 'detailed'
        }
        if ($state.Calls -ne 1 -or ($state.Arguments -join '|') -ne ($expectedArguments -join '|')) {
            throw "Unexpected tool restore arguments: $($state.Arguments -join ' ')"
        }
    }
    finally {
        if ($null -eq $originalDotnet) {
            Remove-Item Function:global:dotnet
        } else {
            Set-Item Function:global:dotnet -Value $originalDotnet.ScriptBlock
        }
    }

    Write-Host "PASS: Tool restore arguments (attempts=$RestoreRetryCount, interactive=$Interactive)"
}

Test-ToolRestoreArguments
Test-ToolRestoreArguments -RestoreRetryCount 1
Test-ToolRestoreArguments -RestoreRetryCount 3
Test-ToolRestoreArguments -RestoreRetryCount 3 -Interactive

# Exercise both restore commands with the real bootstrap while avoiding installs and network access.
function Test-RestoreConfiguration {
    Param([switch]$AzurePipelines, [switch]$LocalMicroBuild)

    $state = [pscustomobject]@{ Calls = [Collections.Generic.List[object]]::new() }
    $originalDotnet = Get-Item Function:global:dotnet -ErrorAction SilentlyContinue
    $originalEnvironment = @{}
    foreach ($name in @('TF_BUILD', 'EnableMicroBuild', 'SignType', 'GITHUB_ACTIONS', 'CmdEnvScriptPath')) {
        $originalEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $null)
    }

    try {
        if ($AzurePipelines) { $env:TF_BUILD = 'true' }
        if ($LocalMicroBuild) { $env:EnableMicroBuild = 'true' }
        Set-Item Function:global:dotnet -Value {
            $state.Calls.Add(@($args))
            $global:LASTEXITCODE = 0
        }.GetNewClosure()
        & $initPath -NoPrerequisites

        $expectedRestore = @('restore')
        $expectedTools = @('tool', 'restore')
        if ($AzurePipelines -or $LocalMicroBuild) {
            $configArguments = @('--configfile', (Join-Path (Split-Path $initPath) 'azure-pipelines/nuget.config'))
            $expectedRestore += $configArguments
            $expectedTools += $configArguments
        }
        $expectedRestore += "-p:EnableMicroBuild=$($AzurePipelines -or $LocalMicroBuild)"

        if ($state.Calls.Count -ne 2 -or ($state.Calls[0] -join '|') -ne ($expectedRestore -join '|') -or ($state.Calls[1] -join '|') -ne ($expectedTools -join '|')) {
            throw "Unexpected restore configuration (AzurePipelines=$AzurePipelines, LocalMicroBuild=$LocalMicroBuild)."
        }
    }
    finally {
        if ($null -eq $originalDotnet) {
            Remove-Item Function:global:dotnet
        } else {
            Set-Item Function:global:dotnet -Value $originalDotnet.ScriptBlock
        }
        foreach ($name in $originalEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $originalEnvironment[$name])
        }
    }

    Write-Host "PASS: Restore configuration (AzurePipelines=$AzurePipelines, LocalMicroBuild=$LocalMicroBuild)"
}

Test-RestoreConfiguration
Test-RestoreConfiguration -AzurePipelines
Test-RestoreConfiguration -LocalMicroBuild
Write-Host 'All restore retry regression checks passed.'
exit 0
