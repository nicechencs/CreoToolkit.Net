#requires -Version 5.1
<#
.SYNOPSIS
  Launch Creo with a CreoToolkit ICreoApplication loaded from deploy/apps/<App>/.

.DESCRIPTION
  这是 Stage/Deploy 形态的统一启动器。**通常不直接跑,由 deploy/start.bat 调入**。
  开发期也应先运行 stage.ps1 + deploy.ps1；旧 manual-launch-samples.ps1 依赖已退役的
  NativeHost/out/managed 布局，仅保留作历史诊断参考。

  本脚本做的事 (按"新手友好"准则,任何失败都给修复指令):
    1. 路径锚: 推断 deploy/ 根 (脚本所在 launcher/ → ..)
    2. -App: 必需,定位 deploy/apps/<App>/<App>.dll (惯例 = ProtkAppls 等)
    3. 找 Creo: -CreoLauncher / env CTK_CREO_LAUNCHER / 内置默认
    4. 写 protk.dat 到 WorkDir (改写 exec_file 到 deploy/native/, text_dir 到 deploy/apps/<App>/text)
    5. 注 CTK_APP_* / CTK_HOST_* / log env (客户无需自己记 env 名)
    6. 启 Creo,等退出,tail log

.PARAMETER App
  必需。要装载的 app 名 (= deploy/apps/<App>/ 目录名)。
  示例: protk-samples-winforms (= deploy/apps/protk-samples-winforms/CreoToolkit.Samples.ProtkAppls.WinForms.dll)

.PARAMETER CreoLauncher
  Creo 启动器 parametric.bat 的完整路径。
  优先级: -CreoLauncher > env CTK_CREO_LAUNCHER > 内置默认。
  内置默认: <Creo 安装路径>\Parametric\bin\parametric.bat

.PARAMETER WorkDir
  Creo 工作目录(protk.dat 会写到这里)。默认: %USERPROFILE%\CreoWorkDir,不存在则自建。
  也可设 env CTK_WORK_DIR 覆盖。

.PARAMETER Model
  Sample 目标模型名(不含 .prt)，写入 CTK_APP_MODEL_NAME 供命令 handler 使用。
  默认: exercise4。传 -Model 或预置 CTK_HOST_SMOKE_MODEL / CTK_APP_LOAD_MODEL_PATH
  才会走 Diagnostics 预载（CTK_APP_LOAD_MODEL_PATH）；否则只设名称，不自动打开模型。

.PARAMETER NoTrace
  开关,默认 trace 级日志 + text+json 双写。
  传 -NoTrace 切换到生产 warn + json (减少日志量,推荐部署稳定后用)。

.EXAMPLE
  start.bat
  双击运行 - 用默认 app + 默认 Creo + 默认 WorkDir。

.EXAMPLE
  start.bat customer-x
  装载 customer-x app (= deploy/apps/customer-x/)。

.EXAMPLE
  launcher\launch.ps1 -App protk-samples -CreoLauncher "<path-to-parametric.bat>" -Model my_part
  完整参数显式调用。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$App,
    [string]$CreoLauncher = $env:CTK_CREO_LAUNCHER,
    [string]$WorkDir = $env:CTK_WORK_DIR,
    [string]$Model = $env:CTK_HOST_SMOKE_MODEL,
    [switch]$NoTrace
)

$ErrorActionPreference = 'Stop'
$PSDefaultParameterValues['Out-File:Encoding'] = 'utf8'

$preloadRequested = $PSBoundParameters.ContainsKey('Model') `
    -or -not [string]::IsNullOrWhiteSpace($env:CTK_HOST_SMOKE_MODEL) `
    -or -not [string]::IsNullOrWhiteSpace($env:CTK_APP_LOAD_MODEL_PATH)
$hadLoadModelPath = -not [string]::IsNullOrWhiteSpace($env:CTK_APP_LOAD_MODEL_PATH)

# ---- 路径锚 ----
$LauncherDir = $PSScriptRoot
$DeployRoot  = (Resolve-Path (Join-Path $LauncherDir '..')).Path
$NativeDir   = Join-Path $DeployRoot 'native'
$ManagedDir  = Join-Path $DeployRoot 'managed'
$AppsDir     = Join-Path $DeployRoot 'apps'
$AppDir      = Join-Path $AppsDir $App

# ---- 默认值 ----
# Creo 路径每个客户机器不同 (PTC 装哪盘哪版),不提供 hardcoded 默认 - 强制由 env / 参数注入,
# 缺失时下面校验段会引导用户 set CTK_CREO_LAUNCHER。
# WorkDir 默认 %USERPROFILE%\CreoWorkDir,跨机器友好,无 hardcoded 盘符。
if ([string]::IsNullOrWhiteSpace($WorkDir)) {
    $WorkDir = Join-Path $env:USERPROFILE 'CreoWorkDir'
}
if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = 'exercise4'
}

# ---- 0. deploy/ 锚 sanity check (launch.ps1 仅供 deploy/launcher/ 调入) ----
if (-not (Test-Path -LiteralPath (Join-Path $DeployRoot 'native')) -or
    -not (Test-Path -LiteralPath (Join-Path $DeployRoot 'managed'))) {
    Write-Host ''
    Write-Host "[X] 当前路径不是 deploy/ 根: $DeployRoot  (exit 29)" -ForegroundColor Red
    Write-Host ''
    Write-Host '    可能原因: launch.ps1 应位于 deploy/launcher/ 内,由 deploy/start.bat 调入。'
    Write-Host '              直接从 scripts/ 下跑 launch.ps1 不工作(scripts/ 上层不是 deploy/)。'
    Write-Host '    修复方法: 先跑 scripts\publish-deploy.ps1 生成 deploy/,再 deploy\start.bat。'
    Write-Host ''
    exit 29
}

# ---- 1. 校验 Creo ----
if ([string]::IsNullOrWhiteSpace($CreoLauncher) -or -not (Test-Path -LiteralPath $CreoLauncher)) {
    Write-Host ''
    if ([string]::IsNullOrWhiteSpace($CreoLauncher)) {
        Write-Host "[X] 没设 Creo 启动器路径  (exit 21)" -ForegroundColor Red
    } else {
        Write-Host "[X] 找不到 Creo: $CreoLauncher  (exit 21)" -ForegroundColor Red
    }
    Write-Host ''
    Write-Host '    可能原因: Creo 未安装,或安装路径未告知 launcher。'
    Write-Host '    修复方法: 设环境变量指向你的 parametric.bat,然后重跑:'
    Write-Host ''
    Write-Host '        set CTK_CREO_LAUNCHER=<你的 Creo 安装根>\Parametric\bin\parametric.bat' -ForegroundColor Yellow
    Write-Host '        start.bat' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '    示例(替换 <Creo install dir> 为实际安装路径):'
    Write-Host '        set CTK_CREO_LAUNCHER=<Creo install dir>\Parametric\bin\parametric.bat'
    Write-Host ''
    exit 21
}

# ---- 2. 校验 deploy/ 完整性 ----
$nativeHost = Join-Path $NativeDir 'CreoToolkit.NativeHost.dll'
if (-not (Test-Path -LiteralPath $nativeHost)) {
    Write-Host "[X] 缺失:$nativeHost" -ForegroundColor Red
    Write-Host '    修复:重跑 scripts\publish-deploy.ps1 -Clean'
    exit 22
}
$managedHost = Join-Path $ManagedDir 'CreoToolkit.Host.dll'
if (-not (Test-Path -LiteralPath $managedHost)) {
    Write-Host "[X] 缺失:$managedHost" -ForegroundColor Red
    Write-Host '    修复:重跑 scripts\publish-deploy.ps1 -Clean'
    exit 23
}

# ---- 3. Resolve app manifest or fallback dll convention ----
if (-not (Test-Path -LiteralPath $AppDir)) {
    Write-Host "[X] App not found: $App ($AppDir)" -ForegroundColor Red
    if (Test-Path -LiteralPath $AppsDir) {
        $available = Get-ChildItem -LiteralPath $AppsDir -Directory | ForEach-Object { $_.Name }
        if ($available.Count -gt 0) {
            Write-Host "    Available apps: $($available -join ', ')"
            Write-Host "    Fix: start-<app>.bat or start.bat <app>"
        } else {
            Write-Host '    deploy/apps/ is empty; run scripts\publish-deploy.ps1 first'
        }
    }
    exit 24
}

$msgFile = 'protk_samples.txt'
$manifestPath = Join-Path $AppDir 'app.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    Write-Host "[X] App manifest not found: $manifestPath" -ForegroundColor Red
    Write-Host '    Fix: re-run scripts\stage.ps1 (it writes apps/<id>/app.json).'
    Write-Host '    Do not scan the app directory for a plugin DLL.'
    exit 25
}
try {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
} catch {
    Write-Host "[X] Invalid app manifest: $manifestPath" -ForegroundColor Red
    Write-Host "    $($_.Exception.Message)"
    exit 25
}

if ([string]::IsNullOrWhiteSpace($manifest.type) -or
    [string]::IsNullOrWhiteSpace($manifest.assembly)) {
    Write-Host "[X] app.json must define type and assembly: $manifestPath" -ForegroundColor Red
    exit 26
}

$env:CTK_APP_TYPE = $manifest.type
$AppAssembly = Join-Path $AppDir $manifest.assembly
if (-not [string]::IsNullOrWhiteSpace($manifest.msgFile)) {
    $msgFile = $manifest.msgFile
}

if (-not (Test-Path -LiteralPath $AppAssembly)) {
    Write-Host "[X] App assembly not found: $AppAssembly" -ForegroundColor Red
    exit 25
}
$appDll = Get-Item -LiteralPath $AppAssembly
$appType = $env:CTK_APP_TYPE

$textDir = Join-Path $AppDir 'text'
$msgFullPath = Join-Path $textDir "usascii\$msgFile"
if (-not (Test-Path -LiteralPath $msgFullPath)) {
    Write-Host "[!] msg file not found: $msgFullPath" -ForegroundColor Yellow
    Write-Host '    Creo menu labels/help may fail with PRO_TK_MSG_NOT_FOUND'
    Write-Host '    Fix: ensure app text files are copied to deploy/apps/<app>/text/usascii/'
}

# ---- 4. Prepare WorkDir ----
if (-not (Test-Path -LiteralPath $WorkDir)) {
    New-Item -ItemType Directory -Path $WorkDir -Force | Out-Null
}
$LogDir = Join-Path $WorkDir 'logs'
if (-not (Test-Path -LiteralPath $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

# ---- 5. 写 protk.dat ----
$protkSrc = Join-Path $NativeDir 'protk.host.dat'
$protkDst = Join-Path $WorkDir 'protk.dat'
if (-not (Test-Path -LiteralPath $protkSrc)) {
    Write-Host "[X] 缺失:$protkSrc" -ForegroundColor Red
    Write-Host '    修复:重跑 scripts\publish-deploy.ps1 -Clean (NativeHost build event 应已写)'
    exit 27
}
$content = Get-Content -LiteralPath $protkSrc -Raw
# 改写 exec_file 到 deploy/native/CreoToolkit.NativeHost.dll
$content = [System.Text.RegularExpressions.Regex]::Replace(
    $content, '^exec_file\s+.+$', "exec_file $nativeHost",
    [System.Text.RegularExpressions.RegexOptions]::Multiline)
# 改写 text_dir 到 deploy/apps/<app>/text
$content = [System.Text.RegularExpressions.Regex]::Replace(
    $content, '^text_dir\s+.+$', "text_dir $textDir",
    [System.Text.RegularExpressions.RegexOptions]::Multiline)
[System.IO.File]::WriteAllText($protkDst, $content, [System.Text.UTF8Encoding]::new($false))

# ---- 6. env 注入 ----
$env:CTK_HOST_METHOD = 'InitializeApp'
$env:CTK_HOST_ASSEMBLY = $managedHost
$env:CTK_APP_TYPE = $appType
$env:CTK_APP_ASSEMBLY = $appDll.FullName
$env:CTK_APP_ALLOWED_ROOTS = $AppDir
$env:CTK_APP_MSG_FILE = $msgFile
$env:CTK_HOST_SMOKE_MODEL = $Model
$env:CTK_APP_MODEL_NAME = $Model
$env:CTK_APP_MODEL_TYPE = 'part'
if (-not $hadLoadModelPath) {
    if ($preloadRequested) {
        $env:CTK_APP_LOAD_MODEL_PATH = (Join-Path $WorkDir "$Model.prt")
    } else {
        Remove-Item Env:CTK_APP_LOAD_MODEL_PATH -ErrorAction SilentlyContinue
    }
}
$env:CTK_HOST_ALLOW_MODEL_WRITE = '1'

$ManagedLog = Join-Path $LogDir 'host-managed.log'
$CtkLogBase = Join-Path $LogDir 'ctk-log'

$env:CTK_HOST_MANAGED_LOG = $ManagedLog
# v4.1: native-bootstrap JSONL 落在 CTK_BOOTSTRAP_LOG_DIR(旧名 CTK_HOST_LOG_DIR 已 deprecated)
$env:CTK_BOOTSTRAP_LOG_DIR = $LogDir

$env:CTK_DOTNET_LOG = 'on'
$env:CTK_DOTNET_LOG_LEVEL = if ($NoTrace) { 'warn' } else { 'trace' }
$env:CTK_DOTNET_LOG_FORMAT = if ($NoTrace) { 'json' } else { 'both' }
$env:CTK_DOTNET_LOG_FILE = $CtkLogBase
$env:CTK_MESSAGEBAR_MIRROR = 'on'

$env:CTK_APP_RUN_COMMAND = $null  # 显式清,等用户点菜单

# ---- 7. Banner ----
Write-Host ''
Write-Host '============================================================'
Write-Host " CreoToolkit launcher - app = $App"
Write-Host '============================================================'
Write-Host "Creo launcher:    $CreoLauncher"
Write-Host "Deploy root:      $DeployRoot"
Write-Host "App dll:          $($appDll.Name)"
Write-Host "App type:         $appType"
Write-Host "WorkDir:          $WorkDir"
Write-Host "protk.dat ->     $protkDst"
Write-Host "Model name:       $Model"
Write-Host "Model preload:    $(if ($env:CTK_APP_LOAD_MODEL_PATH) { $env:CTK_APP_LOAD_MODEL_PATH } else { '(off; pass -Model or set CTK_APP_LOAD_MODEL_PATH)' })"
Write-Host "Managed log:      $ManagedLog"
Write-Host "Trace:            $(if ($NoTrace) {'off (warn+json)'} else {'on (trace+both)'})"
Write-Host ''
Write-Host '启动 Creo - 退出 Creo 后本脚本会 tail managed log 末 40 行。'
Write-Host ''

# ---- 8. 启动 Creo ----
Push-Location $WorkDir
try {
    & $CreoLauncher
    $rc = $LASTEXITCODE
}
finally {
    Pop-Location
}

# ---- 9. Tail log ----
Write-Host ''
Write-Host "=== host-managed.log (last 40 lines) ==="
$dated = Join-Path $LogDir ("host-managed-{0}.log" -f (Get-Date -Format 'yyyy-MM-dd'))
if (Test-Path -LiteralPath $dated) {
    Get-Content -LiteralPath $dated -Tail 40
} elseif (Test-Path -LiteralPath $ManagedLog) {
    Get-Content -LiteralPath $ManagedLog -Tail 40
} else {
    Write-Host "[!] managed log 未产生 ($ManagedLog / $dated)" -ForegroundColor Yellow
    Write-Host '    可能 Creo 启动失败或 Bootstrap 未到日志绑定步骤'
}

exit $rc
