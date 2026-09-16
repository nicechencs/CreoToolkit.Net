#requires -Version 5.1
<#
.SYNOPSIS
  Compatibility entry point for Stage -> Deploy.

.DESCRIPTION
  New automation should invoke stage.ps1 and deploy.ps1 separately. This wrapper
  preserves the historical publish-deploy.ps1 command line while delegating all
  materialization and copy rules to those two scripts.
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Clean,
    [switch]$SkipBuild,
    [switch]$SkipNativeBuild,
    [switch]$Zip,
    [string]$StageRoot,
    [string]$DeployRoot
)

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($StageRoot)) {
    $StageRoot = Join-Path $RepoRoot ("artifacts\stage\{0}" -f $Configuration)
}
if ([string]::IsNullOrWhiteSpace($DeployRoot)) {
    $DeployRoot = Join-Path $RepoRoot 'deploy'
}

if ($Clean) {
    Write-Host '[compat] -Clean is accepted; stage.ps1 always replaces its stage atomically.' -ForegroundColor DarkGray
}

$stageArgs = @('-Configuration', $Configuration, '-StageRoot', $StageRoot)
if ($SkipBuild) {
    $stageArgs += '-SkipBuild'
}
elseif (-not $SkipNativeBuild) {
    $stageArgs += '-BuildNative'
}

& (Join-Path $PSScriptRoot 'stage.ps1') @stageArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$deployArgs = @('-StageRoot', $StageRoot, '-Destination', $DeployRoot)
if ($Zip) { $deployArgs += '-Zip' }
& (Join-Path $PSScriptRoot 'deploy.ps1') @deployArgs
exit $LASTEXITCODE
