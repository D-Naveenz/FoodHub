#requires -Version 7.4
<#
.SYNOPSIS
Checks database automation control flow using a fake dotnet command; never opens SQL.
.DESCRIPTION
Run from any directory. The scoped fake records command arguments and injected exit codes, so reset
ordering, target restrictions, WhatIf and fail-fast behavior can be verified without database access.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$automation = Join-Path $PSScriptRoot 'Invoke-Database.ps1'
$automationProbe = @{ Calls = [Collections.Generic.List[object]]::new(); ExitCode = 0 }
$originalLocation = (Get-Location).Path
$exitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
$hadExitCode = $null -ne $exitCodeVariable
$previousExitCode = if ($hadExitCode) { $exitCodeVariable.Value } else { $null }

# This function shadows the native executable only within this test script's scope.
function dotnet {
    [void] $automationProbe.Calls.Add(@($args))
    $global:LASTEXITCODE = $automationProbe.ExitCode
}
function Assert-Condition {
    param([bool] $Condition, [string] $Description)
    if (-not $Condition) { throw "Failed: $Description" }
}
function Assert-Rejected {
    param([scriptblock] $Operation)
    $rejected = $false
    try { & $Operation } catch { $rejected = $true }
    Assert-Condition $rejected 'The operation must fail.'
}
try {
    & $automation Reset -WhatIf
    Assert-Condition ($automationProbe.Calls.Count -eq 0) 'WhatIf must run no restore/build/EF command.'
    foreach ($target in @(@('remote', 'FoodHub_Dev'), @('localhost', 'master'), @('localhost', 'FoodHub_Production'))) {
        Assert-Rejected { & $automation Reset -Server $target[0] -Database $target[1] -NoBuild -NoRestore }
        Assert-Condition ($automationProbe.Calls.Count -eq 0) 'Invalid reset must fail before any native command.'
    }
    & $automation Reset -NoBuild -NoRestore
    Assert-Condition ($automationProbe.Calls.Count -eq 2) 'Reset must drop and then migrate.'
    Assert-Condition ($automationProbe.Calls[0] -contains 'drop') 'First reset step must drop.'
    Assert-Condition ($automationProbe.Calls[0] -contains '--force') 'Explicit reset must be noninteractive.'
    Assert-Condition ($automationProbe.Calls[1] -contains 'update') 'Second reset step must migrate.'
    foreach ($call in $automationProbe.Calls) {
        Assert-Condition ($call -contains '--reset') 'Both reset steps must enable the factory guard.'
        Assert-Condition ($call -contains 'FoodHub_Dev') 'Both steps must use the same database.'
    }
    $automationProbe.Calls.Clear()
    $automationProbe.ExitCode = 23
    Assert-Rejected { & $automation Reset -NoBuild -NoRestore }
    Assert-Condition ($automationProbe.Calls.Count -eq 1) 'A failed drop must prevent migration.'
    Assert-Condition ((Get-Location).Path -eq $originalLocation) 'Failure must restore the caller directory.'
    $automationProbe.Calls.Clear()
    Assert-Rejected { & $automation Migrate }
    Assert-Condition ($automationProbe.Calls.Count -eq 1) 'A failed tool restore must prevent build and SQL.'
    $automationProbe.Calls.Clear()
    $automationProbe.ExitCode = 0
    & $automation Info -Server 'fictional server;value' -Database 'fictional database' -NoBuild -NoRestore
    Assert-Condition ($automationProbe.Calls.Count -eq 1) 'Info must invoke EF once.'
    Assert-Condition ($automationProbe.Calls[0] -contains 'fictional server;value') 'Server arguments must remain single values.'
    Assert-Condition ($automationProbe.Calls[0] -contains 'fictional database') 'Database arguments must remain single values.'
    Write-Host 'Database automation control-flow checks passed. No SQL was executed.'
}
finally {
    if (-not $hadExitCode) { Remove-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue }
    else { $global:LASTEXITCODE = $previousExitCode }
}
