#requires -Version 7.4
<#
.SYNOPSIS
Runs EF Core automation without launching FoodHub or a separate database executable.
.DESCRIPTION
Migrate applies pending migrations. Reset drops and rebuilds only localhost/FoodHub_Dev, without
backups or seed data. Info inspects context configuration without opening SQL. Script exports an
idempotent migration script without executing SQL. All actions support -WhatIf before tool restore/build.
Targets use explicit Windows-authentication parameters, independently of the application's settings.
.PARAMETER Action
Migrate, Reset, Info or Script. Select an action explicitly; there is no default database operation.
.PARAMETER Server
SQL Server host/instance; defaults to localhost. Reset accepts only the exact localhost name.
.PARAMETER Database
Database catalog; defaults to FoodHub_Dev. Reset accepts only this disposable development catalog.
.PARAMETER Configuration
Build configuration for Core; defaults to Debug. It does not silently select a different database.
.PARAMETER OutputPath
Destination for Script; defaults to artifacts/FoodHub.Migrations.sql relative to the repository.
.PARAMETER NoBuild
Use existing Core build output. Use after building the selected configuration yourself.
.PARAMETER NoRestore
Skip tool/project restore when dependencies have already been restored.
.EXAMPLE
./scripts/Invoke-Database.ps1 Migrate
.EXAMPLE
./scripts/Invoke-Database.ps1 Reset -WhatIf
.EXAMPLE
./scripts/Invoke-Database.ps1 Script -OutputPath ./artifacts/review.sql
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidateSet('Migrate', 'Reset', 'Info', 'Script')]
    [string] $Action,
    [ValidateNotNullOrEmpty()]
    [string] $Server = 'localhost',
    [ValidateNotNullOrEmpty()]
    [string] $Database = 'FoodHub_Dev',
    [ValidateNotNullOrEmpty()]
    [string] $Configuration = 'Debug',
    [string] $OutputPath,
    [switch] $NoBuild,
    [switch] $NoRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repository = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repository 'FoodHub.Core/FoodHub.Core.csproj'

# Validate before restoring tools, building, or opening any database connection. The factory repeats
# this guard through --reset, using SqlConnectionStringBuilder and the existing reset-target policy.
if ($Action -eq 'Reset' -and ($Server -ine 'localhost' -or $Database -ine 'FoodHub_Dev')) {
    throw 'Reset is allowed only for Server=localhost;Database=FoodHub_Dev.'
}
if ($Action -ne 'Script' -and $PSBoundParameters.ContainsKey('OutputPath')) {
    throw 'OutputPath applies only to the Script action.'
}
if ($Action -eq 'Script') {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $repository 'artifacts/FoodHub.Migrations.sql' }
    # Resolve relative output paths against the caller before entering the repository for tool discovery.
    $OutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
}
if (-not $PSCmdlet.ShouldProcess("$Server/$Database", "$Action with EF Core")) { return }

function Invoke-DotNet {
    <# .SYNOPSIS Runs one native command and stops immediately on a nonzero exit code. #>
    param([string[]] $CommandArguments)
    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE. No subsequent database step was run." }
}
function Invoke-Ef {
    <# .SYNOPSIS Keeps project selection and factory arguments identical for every step, including reset. #>
    param([string[]] $EfArguments)
    $factoryArguments = @('--server', $Server, '--database', $Database)
    if ($Action -eq 'Reset') { $factoryArguments += '--reset' }
    Invoke-DotNet -CommandArguments (@('tool', 'run', 'dotnet-ef', '--') + $EfArguments + @(
        '--project', $project, '--startup-project', $project, '--configuration', $Configuration,
        '--context', 'FoodHub_DevContext', '--no-build', '--') + $factoryArguments)
}

Push-Location -LiteralPath $repository
try {
    if (-not $NoRestore) { Invoke-DotNet -CommandArguments @('tool', 'restore') }
    if (-not $NoBuild) {
        $buildArguments = @('build', $project, '--configuration', $Configuration, '-p:UseSharedCompilation=false')
        if ($NoRestore) { $buildArguments += '--no-restore' }
        Invoke-DotNet -CommandArguments $buildArguments
    }
    switch ($Action) {
        'Info' { Invoke-Ef -EfArguments @('dbcontext', 'info') }
        'Script' {
            [void] [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath))
            Invoke-Ef -EfArguments @('migrations', 'script', '--idempotent', '--output', $OutputPath)
        }
        'Migrate' { Invoke-Ef -EfArguments @('database', 'update') }
        'Reset' {
            # Reset is an explicit destructive action. --force avoids a second interactive prompt;
            # ShouldProcess still supports -WhatIf/-Confirm. A failed drop prevents the update step.
            Invoke-Ef -EfArguments @('database', 'drop', '--force')
            Invoke-Ef -EfArguments @('database', 'update')
        }
    }
}
finally { Pop-Location }
