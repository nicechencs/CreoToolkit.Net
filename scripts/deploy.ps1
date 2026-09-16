#requires -Version 5.1
<#
.SYNOPSIS
  Verifies and publishes an existing CreoToolkit stage to a selected destination.

.DESCRIPTION
  Deploy never builds. It verifies stage-manifest.json, copies through a sibling
  temporary directory, then replaces the destination. Exit codes: 71 stage missing;
  72 stage verification failed; 73 unsafe/invalid destination; 74 copy verification
  failed; 75 zip creation failed.
#>

[CmdletBinding()]
param(
    [string]$StageRoot,
    [string]$Destination,
    [switch]$Zip
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($StageRoot)) {
    $StageRoot = Join-Path $RepoRoot 'artifacts\stage\Release'
}
if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $RepoRoot 'deploy'
}
$StageRoot = [System.IO.Path]::GetFullPath($StageRoot).TrimEnd('\', '/')
$Destination = [System.IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
$DeployTemp = "$Destination.tmp-$PID"
$RollbackRoot = "$Destination.rollback-$PID"

function Stop-Deploy {
    param([int]$Code, [string]$Message)
    Write-Host "[X] $Message (exit $Code)" -ForegroundColor Red
    exit $Code
}

function Assert-SafeDestination {
    param([string]$Path)
    $root = [System.IO.Path]::GetPathRoot($Path).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path -eq $root -or
        $Path -eq $RepoRoot.TrimEnd('\', '/') -or
        $Path -eq $StageRoot) {
        Stop-Deploy 73 "Refusing unsafe deploy destination '$Path'"
    }
}

function Read-VerifiedManifest {
    param([string]$Root)
    $manifestPath = Join-Path $Root 'stage-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "stage-manifest.json is missing under '$Root'"
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $null -eq $manifest.files) {
        throw 'unsupported or incomplete stage manifest'
    }
    foreach ($entry in $manifest.files) {
        $relative = ([string]$entry.path).Replace('/', '\')
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))
        $prefix = $Root + [System.IO.Path]::DirectorySeparatorChar
        if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "manifest path escapes stage root: $relative"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "manifest file is missing: $relative"
        }
        $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$entry.sha256).ToLowerInvariant()) {
            throw "SHA256 mismatch: $relative"
        }
    }
    return $manifest
}

if (-not (Test-Path -LiteralPath $StageRoot -PathType Container)) {
    Stop-Deploy 71 "Stage directory does not exist: $StageRoot"
}
Assert-SafeDestination $Destination
Assert-SafeDestination $DeployTemp
Assert-SafeDestination $RollbackRoot
try {
    $manifest = Read-VerifiedManifest $StageRoot
}
catch {
    Stop-Deploy 72 "Stage verification failed: $($_.Exception.Message)"
}

try {
    if (Test-Path -LiteralPath $DeployTemp) {
        Remove-Item -LiteralPath $DeployTemp -Recurse -Force
    }
    if (Test-Path -LiteralPath $RollbackRoot) {
        Remove-Item -LiteralPath $RollbackRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $DeployTemp -Force | Out-Null
    Get-ChildItem -LiteralPath $StageRoot -Force | Copy-Item -Destination $DeployTemp -Recurse -Force
    Read-VerifiedManifest $DeployTemp | Out-Null

    if (Test-Path -LiteralPath $Destination) {
        New-Item -ItemType Directory -Path $RollbackRoot -Force | Out-Null
        Get-ChildItem -LiteralPath $Destination -Force | Copy-Item -Destination $RollbackRoot -Recurse -Force
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $DeployTemp -Force | Copy-Item -Destination $Destination -Recurse -Force
    Remove-Item -LiteralPath $DeployTemp -Recurse -Force
    Read-VerifiedManifest $Destination | Out-Null
    if (Test-Path -LiteralPath $RollbackRoot) {
        Remove-Item -LiteralPath $RollbackRoot -Recurse -Force
    }
}
catch {
    if (Test-Path -LiteralPath $DeployTemp) {
        Remove-Item -LiteralPath $DeployTemp -Recurse -Force
    }
    if (Test-Path -LiteralPath $RollbackRoot) {
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Recurse -Force
        }
        New-Item -ItemType Directory -Path $Destination -Force | Out-Null
        Get-ChildItem -LiteralPath $RollbackRoot -Force | Copy-Item -Destination $Destination -Recurse -Force
        Remove-Item -LiteralPath $RollbackRoot -Recurse -Force
    }
    Stop-Deploy 74 "Deploy copy failed: $($_.Exception.Message)"
}

if ($Zip) {
    try {
        $zipPath = "$Destination.zip"
        if (Test-Path -LiteralPath $zipPath) {
            Remove-Item -LiteralPath $zipPath -Force
        }
        Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "[OK] Package ready: $zipPath" -ForegroundColor Green
    }
    catch {
        Stop-Deploy 75 "Zip creation failed: $($_.Exception.Message)"
    }
}

Write-Host "[OK] Deploy ready: $Destination" -ForegroundColor Green
Write-Host "     Apps: $(@($manifest.apps) -join ', ')"
exit 0
