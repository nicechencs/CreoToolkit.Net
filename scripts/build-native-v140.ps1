[CmdletBinding()]
param(
    [string]$Target,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$ProbeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$BuildDir = Join-Path $RepoRoot 'cmake-v140'

function Resolve-CMakeExe {
    $candidates = @()
    if ($env:CMAKE_EXE) {
        $candidates += $env:CMAKE_EXE
    }

    $onPath = Get-Command cmake.exe -ErrorAction SilentlyContinue
    if ($onPath) {
        $candidates += $onPath.Source
    }

    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere -PathType Leaf) {
        $installations = @(& $vswhere -all -products * -property installationPath)
        foreach ($installation in $installations) {
            if ([string]::IsNullOrWhiteSpace($installation)) { continue }
            $candidates += Join-Path $installation 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
        }
    }

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            $resolved = (Resolve-Path -LiteralPath $candidate).Path
            $versionText = @(& $resolved --version 2>$null) | Select-Object -First 1
            if ($versionText -match 'cmake version (\d+\.\d+\.\d+)' -and
                [version]$Matches[1] -ge [version]'3.25.0') {
                return $resolved
            }
        }
    }

    throw 'cmake.exe was not found. Set CMAKE_EXE or install CMake 3.25+.'
}

function Resolve-Vc140Root {
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $candidates = @()
    if ($env:CTK_VC140_ROOT) {
        $candidates += $env:CTK_VC140_ROOT
    }
    $candidates += Join-Path $programFilesX86 'Microsoft Visual Studio 14.0\VC'

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        $required = @(
            (Join-Path $candidate 'vcvarsall.bat'),
            (Join-Path $candidate 'bin\amd64\cl.exe'),
            (Join-Path $candidate 'bin\amd64\link.exe'),
            (Join-Path $candidate 'bin\amd64\nmake.exe'),
            (Join-Path $candidate 'lib\amd64\msvcrt.lib')
        )
        if (@($required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -eq 0) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw 'VC140 x64 tools were not found. Install Visual C++ 2015 Update 3 or set CTK_VC140_ROOT.'
}

function Test-WindowsSdkLayout {
    param(
        [string]$Root,
        [string]$Version
    )

    $required = @(
        (Join-Path $Root "Include\$Version\ucrt\wchar.h"),
        (Join-Path $Root "Include\$Version\shared\sdkddkver.h"),
        (Join-Path $Root "Include\$Version\um\Windows.h"),
        (Join-Path $Root "Lib\$Version\ucrt\x64\ucrt.lib"),
        (Join-Path $Root "Lib\$Version\um\x64\kernel32.lib"),
        (Join-Path $Root "bin\$Version\x64\rc.exe"),
        (Join-Path $Root "bin\$Version\x64\mt.exe")
    )
    if (@($required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }).Count -ne 0) {
        return $false
    }

    # Windows SDK 10.0.26100 的 UCRT 头用到 VC140 不具备的 intrinsic，排除该版本。
    $wchar = Join-Path $Root "Include\$Version\ucrt\wchar.h"
    if (Select-String -LiteralPath $wchar -SimpleMatch '_mm_loadu_si64' -Quiet) {
        return $false
    }
    return $true
}

function Resolve-CompatibleWindowsSdk {
    $programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
    $root = if ($env:CTK_VC140_WINDOWS_SDK_ROOT) {
        $env:CTK_VC140_WINDOWS_SDK_ROOT
    } else {
        Join-Path $programFilesX86 'Windows Kits\10'
    }
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Windows SDK root does not exist: $root"
    }
    $root = (Resolve-Path -LiteralPath $root).Path

    if ($env:CTK_VC140_WINDOWS_SDK_VERSION) {
        $version = $env:CTK_VC140_WINDOWS_SDK_VERSION.TrimEnd('\')
        if (-not (Test-WindowsSdkLayout -Root $root -Version $version)) {
            throw "Windows SDK $version is incomplete or incompatible with VC140."
        }
        return [pscustomobject]@{ Root = $root; Version = $version }
    }

    $versions = @(
        Get-ChildItem -LiteralPath (Join-Path $root 'Include') -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                try {
                    [pscustomobject]@{ Text = $_.Name; Parsed = [version]$_.Name }
                } catch {
                    $null
                }
            } |
            Where-Object { $null -ne $_ } |
            Sort-Object Parsed
    )
    foreach ($version in $versions) {
        if (Test-WindowsSdkLayout -Root $root -Version $version.Text) {
            return [pscustomobject]@{ Root = $root; Version = $version.Text }
        }
    }

    throw 'No complete Windows 10 SDK compatible with VC140 was found. Set CTK_VC140_WINDOWS_SDK_VERSION.'
}

function Import-Vc140Environment {
    param([string]$VcRoot)

    $vcvars = Join-Path $VcRoot 'vcvarsall.bat'
    $command = 'call "' + $vcvars + '" amd64 >nul && set'
    $lines = @(& $env:ComSpec /d /s /c $command)
    if ($LASTEXITCODE -ne 0) {
        throw "vcvarsall.bat failed with exit code $LASTEXITCODE"
    }
    foreach ($line in $lines) {
        $separator = $line.IndexOf('=')
        if ($separator -le 0) { continue }
        $name = $line.Substring(0, $separator)
        $value = $line.Substring($separator + 1)
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }
}

function Get-CMakeCacheValue {
    param(
        [string]$CacheDir,
        [string]$Name
    )
    $cache = Join-Path $CacheDir 'CMakeCache.txt'
    if (-not (Test-Path -LiteralPath $cache -PathType Leaf)) { return $null }
    $pattern = '^{0}:[^=]+=(.*)$' -f [regex]::Escape($Name)
    foreach ($line in Get-Content -LiteralPath $cache) {
        if ($line -match $pattern) { return $Matches[1].Trim() }
    }
    return $null
}

function Get-CMakePresetValue {
    param([string]$Name)
    $presetPath = Join-Path $RepoRoot 'CMakeUserPresets.json'
    if (-not (Test-Path -LiteralPath $presetPath -PathType Leaf)) { return $null }
    try {
        $presets = (Get-Content -LiteralPath $presetPath -Raw | ConvertFrom-Json).configurePresets
        foreach ($preset in @($presets)) {
            if ($null -eq $preset.cacheVariables) { continue }
            $property = $preset.cacheVariables.PSObject.Properties[$Name]
            if ($null -eq $property) { continue }
            $value = $property.Value
            if ($value -is [string]) { return $value }
            if ($null -ne $value.value) { return [string]$value.value }
        }
    } catch {
        return $null
    }
    return $null
}

function Resolve-NativeInput {
    param([string]$Name)
    $candidates = @(
        (Get-CMakeCacheValue -CacheDir $BuildDir -Name $Name),
        [Environment]::GetEnvironmentVariable($Name),
        (Get-CMakeCacheValue -CacheDir (Join-Path $RepoRoot 'cmake') -Name $Name),
        (Get-CMakePresetValue -Name $Name)
    )
    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Container)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw "$Name is not configured or its directory does not exist."
}

$cmake = Resolve-CMakeExe
$vcRoot = Resolve-Vc140Root
$sdk = Resolve-CompatibleWindowsSdk

Write-Host "[VC140] compiler root : $vcRoot"
Write-Host "[VC140] Windows SDK   : $($sdk.Root)\$($sdk.Version)"
Write-Host "[VC140] CMake        : $cmake"

if ($ProbeOnly) {
    return
}
if ([string]::IsNullOrWhiteSpace($Target)) {
    throw 'Target is required unless -ProbeOnly is specified.'
}

Import-Vc140Environment -VcRoot $vcRoot

$sdkInclude = Join-Path $sdk.Root "Include\$($sdk.Version)"
$sdkLib = Join-Path $sdk.Root "Lib\$($sdk.Version)"
$sdkBin = Join-Path $sdk.Root "bin\$($sdk.Version)\x64"
$includeDirs = @(
    (Join-Path $vcRoot 'include'),
    (Join-Path $vcRoot 'atlmfc\include'),
    (Join-Path $sdkInclude 'ucrt'),
    (Join-Path $sdkInclude 'shared'),
    (Join-Path $sdkInclude 'um'),
    (Join-Path $sdkInclude 'winrt'),
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\NETFXSDK\4.8\Include\um'),
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\NETFXSDK\4.7.2\Include\um')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }
$libDirs = @(
    (Join-Path $vcRoot 'lib\amd64'),
    (Join-Path $vcRoot 'atlmfc\lib\amd64'),
    (Join-Path $sdkLib 'ucrt\x64'),
    (Join-Path $sdkLib 'um\x64'),
    (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\NETFXSDK\4.8\Lib\um\x64')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }

$env:PATH = "$sdkBin;$($env:PATH)"
$env:INCLUDE = $includeDirs -join ';'
$env:LIB = $libDirs -join ';'
$env:WindowsSdkDir = $sdk.Root + '\'
$env:WindowsSDKVersion = $sdk.Version + '\'
$env:UCRTVersion = $sdk.Version

$creoRoot = Resolve-NativeInput -Name 'CREO_ROOT'

& $cmake -S $RepoRoot -B $BuildDir -G 'NMake Makefiles' `
    "-DCMAKE_BUILD_TYPE=$Configuration" `
    '-DCTK_EXPECTED_MSVC_VERSION=1900' `
    "-DCREO_ROOT=$creoRoot"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $cmake --build $BuildDir --target $Target
exit $LASTEXITCODE
