#requires -Version 5.1
<#
.SYNOPSIS
  Builds (optionally) and materializes a deterministic CreoToolkit deployment stage.

.DESCRIPTION
  Stage is the only script that reshapes build outputs. It never writes deploy/ or a
  Creo installation. CI may provide a prebuilt NativeHost through -NativeInputDir and
  run this script without a local Creo SDK.

  Exit codes: 81 tool missing; 82 managed build/publish failed; 83 native build failed;
  84 required input missing; 85 stage validation/materialization failed.
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$StageRoot,
    [string]$NativeInputDir,
    [switch]$SkipBuild,
    [switch]$BuildNative
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($StageRoot)) {
    $StageRoot = Join-Path $RepoRoot ("artifacts\stage\{0}" -f $Configuration)
}
if ([string]::IsNullOrWhiteSpace($NativeInputDir)) {
    $NativeInputDir = Join-Path $RepoRoot 'src\CreoToolkit.NativeHost\out'
}
$StageRoot = [System.IO.Path]::GetFullPath($StageRoot)
$NativeInputDir = [System.IO.Path]::GetFullPath($NativeInputDir)
$StageTemp = "$StageRoot.tmp-$PID"

function Stop-Stage {
    param([int]$Code, [string]$Message)
    Write-Host "[X] $Message (exit $Code)" -ForegroundColor Red
    exit $Code
}

function Assert-ReplaceableDirectory {
    param([string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $driveRoot = [System.IO.Path]::GetPathRoot($full).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($full) -or
        $full -eq $driveRoot -or
        $full -eq $RepoRoot.TrimEnd('\', '/')) {
        Stop-Stage 85 "Refusing to replace unsafe stage path '$Path'"
    }
}

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments, [int]$FailureCode)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        Stop-Stage $FailureCode "Command failed ($LASTEXITCODE): $Command $($Arguments -join ' ')"
    }
}

function Copy-RequiredFile {
    param([string]$Source, [string]$Destination)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        Stop-Stage 84 "Required stage input is missing: $Source"
    }
    $parent = Split-Path -Parent $Destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

function Get-PELinkerVersion {
    param([string]$Path)
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4 + 20 + 2
        return @($reader.ReadByte(), $reader.ReadByte())
    }
    finally {
        $stream.Dispose()
    }
}

Assert-ReplaceableDirectory $StageRoot
Assert-ReplaceableDirectory $StageTemp
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Stage 81 'dotnet SDK was not found'
}

if (Test-Path -LiteralPath $StageTemp) {
    Remove-Item -LiteralPath $StageTemp -Recurse -Force
}
New-Item -ItemType Directory -Path $StageTemp -Force | Out-Null

try {
    if (-not $SkipBuild) {
        Write-Host "[Build] dotnet build ($Configuration)" -ForegroundColor Cyan
        Invoke-Checked 'dotnet' @(
            'build', (Join-Path $RepoRoot 'CreoToolkitRefactor.slnx'),
            '-c', $Configuration, '--nologo', '-p:CtkDeployEnabled=false'
        ) 82

        if ($BuildNative) {
            Write-Host "[Build] NativeHost VC140 ($Configuration)" -ForegroundColor Cyan
            Invoke-Checked (Join-Path $RepoRoot 'build_native.cmd') @(
                'CreoToolkit.NativeHost', $Configuration, 'v140'
            ) 83
        }
    }

    $managedDir = Join-Path $StageTemp 'managed'
    Write-Host '[Stage] publish managed host' -ForegroundColor Cyan
    Invoke-Checked 'dotnet' @(
        'publish', (Join-Path $RepoRoot 'src\CreoToolkit.Host\CreoToolkit.Host.csproj'),
        '-c', $Configuration,
        '--no-build', '--nologo', '-p:CtkDeployEnabled=false', '-o', $managedDir
    ) 82
    Get-ChildItem -LiteralPath $managedDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    $catalogPath = Join-Path $RepoRoot 'samples\SampleCatalog.props'
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        Stop-Stage 84 "Sample catalog is missing: $catalogPath"
    }
    & (Join-Path $PSScriptRoot 'validate-sample-catalog.ps1') -CatalogPath $catalogPath
    if ($LASTEXITCODE -ne 0) {
        Stop-Stage 85 "Sample catalog validation failed ($LASTEXITCODE)"
    }
    [xml]$catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8
    $apps = @(
        $catalog.SelectNodes('/Project/ItemGroup/CtkSampleApp') |
            Sort-Object { [int]$_.Order } |
            ForEach-Object {
                [ordered]@{
                    id = [string]$_.Include
                    project = Join-Path 'samples' ([string]$_.ProjectRelativePath)
                    type = [string]$_.Type
                    assembly = [string]$_.Assembly
                    msgFile = [string]$_.MsgFile
                }
            }
    )

    foreach ($app in $apps) {
        $appDir = Join-Path $StageTemp ("apps\{0}" -f $app.id)
        Write-Host "[Stage] publish app $($app.id)" -ForegroundColor Cyan
        Invoke-Checked 'dotnet' @(
            'publish', (Join-Path $RepoRoot $app.project),
            '-c', $Configuration, '--self-contained', 'false',
            '--no-build', '--nologo', '-p:CtkDeployEnabled=false', '-o', $appDir
        ) 82
        Get-ChildItem -LiteralPath $appDir -Filter '*.pdb' -File -ErrorAction SilentlyContinue |
            Remove-Item -Force
        $manifest = [ordered]@{
            type = $app.type
            assembly = $app.assembly
            msgFile = $app.msgFile
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $appDir 'app.json'),
            (($manifest | ConvertTo-Json -Depth 3) + "`n"),
            [System.Text.UTF8Encoding]::new($false))
    }

    $nativeDir = Join-Path $StageTemp 'native'
    Copy-RequiredFile (Join-Path $NativeInputDir 'CreoToolkit.NativeHost.dll') (Join-Path $nativeDir 'CreoToolkit.NativeHost.dll')
    $linkerVersion = Get-PELinkerVersion (Join-Path $nativeDir 'CreoToolkit.NativeHost.dll')
    if ($linkerVersion[0] -ne 14 -or $linkerVersion[1] -ge 10) {
        Stop-Stage 85 "NativeHost toolchain mismatch: linker $($linkerVersion[0]).$($linkerVersion[1]); VC140 14.0x is required"
    }

    # Generate the portable stage template; launch.ps1 materializes absolute paths
    # only in the selected Creo work directory.
    $protkSource = Join-Path $RepoRoot 'src\CreoToolkit.NativeHost\protk.host.dat'
    if (-not (Test-Path -LiteralPath $protkSource -PathType Leaf)) {
        Stop-Stage 84 "protk.host.dat source is missing: $protkSource"
    }
    $protk = Get-Content -LiteralPath $protkSource -Raw -Encoding UTF8
    $protk = [regex]::Replace($protk, '^exec_file\s+.+$', 'exec_file __CTK_NATIVE_HOST_DLL__', 'Multiline')
    $protk = [regex]::Replace($protk, '^text_dir\s+.+$', 'text_dir __CTK_TEXT_DIR__', 'Multiline')
    [System.IO.File]::WriteAllText((Join-Path $nativeDir 'protk.host.dat'), $protk, [System.Text.UTF8Encoding]::new($false))

    $launcherDir = Join-Path $StageTemp 'launcher'
    New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
    Copy-RequiredFile (Join-Path $PSScriptRoot 'launch.ps1') (Join-Path $launcherDir 'launch.ps1')

    $startTemplate = @'
@echo off
REM Generated by scripts\stage.ps1. Do not edit.
setlocal
set "CTK_ROOT=%~dp0"
set "CTK_APP=%~1"
if "%CTK_APP%"=="" set "CTK_APP={{APP}}"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%CTK_ROOT%launcher\launch.ps1" -App "%CTK_APP%" %2 %3 %4 %5 %6 %7 %8 %9
exit /b %errorlevel%
'@
    $defaultApp = $apps[0].id
    [System.IO.File]::WriteAllText(
        (Join-Path $StageTemp 'start.bat'),
        ($startTemplate -replace '\{\{APP\}\}', $defaultApp),
        [System.Text.UTF8Encoding]::new($false))
    foreach ($app in $apps) {
        [System.IO.File]::WriteAllText(
            (Join-Path $StageTemp ("start-{0}.bat" -f $app.id)),
            ($startTemplate -replace '\{\{APP\}\}', $app.id),
            [System.Text.UTF8Encoding]::new($false))
    }

    $required = @(
        'native\CreoToolkit.NativeHost.dll',
        'native\protk.host.dat',
        'managed\CreoToolkit.Host.dll',
        'managed\CreoToolkit.Diagnostics.dll',
        'managed\CreoToolkit.App.dll',
        'managed\CreoToolkit.Sdk.dll',
        'managed\CreoToolkit.Interop.dll',
        'launcher\launch.ps1'
    )
    foreach ($app in $apps) {
        $required += "apps\$($app.id)\app.json"
        $required += "apps\$($app.id)\$($app.assembly)"
        $required += "apps\$($app.id)\text\usascii\$($app.msgFile)"
    }
    foreach ($relative in $required) {
        if (-not (Test-Path -LiteralPath (Join-Path $StageTemp $relative) -PathType Leaf)) {
            Stop-Stage 85 "Stage validation failed; missing $relative"
        }
    }

    $fileEntries = @(
        Get-ChildItem -LiteralPath $StageTemp -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($StageTemp.Length).TrimStart('\', '/') -replace '\\', '/'
                [ordered]@{
                    path = $relative
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    length = $_.Length
                }
            }
    )
    $stageManifest = [ordered]@{
        schemaVersion = 1
        configuration = $Configuration
        apps = @($apps | ForEach-Object { $_.id })
        files = $fileEntries
    }
    [System.IO.File]::WriteAllText(
        (Join-Path $StageTemp 'stage-manifest.json'),
        (($stageManifest | ConvertTo-Json -Depth 6) + "`n"),
        [System.Text.UTF8Encoding]::new($false))

    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force
    }
    $parent = Split-Path -Parent $StageRoot
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    New-Item -ItemType Directory -Path $StageRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $StageTemp -Force | Copy-Item -Destination $StageRoot -Recurse -Force
    Remove-Item -LiteralPath $StageTemp -Recurse -Force
    Write-Host "[OK] Stage ready: $StageRoot" -ForegroundColor Green
}
catch {
    if (Test-Path -LiteralPath $StageTemp) {
        Remove-Item -LiteralPath $StageTemp -Recurse -Force
    }
    Write-Host "[X] Stage failed: $($_.Exception.Message) (exit 85)" -ForegroundColor Red
    exit 85
}

exit 0
