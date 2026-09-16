# lint-encoding.ps1 — 编码卫生门（防三类实证过的静默翻车）
#
# 规则（全部来自真实事故，不是审美）：
#   R1 .def       : 只许 ASCII。MSVC 按 ANSI 解析 .def，注释行尾的 UTF-8 多字节
#                   字符会“静默吞掉下一行符号”（2026-07-03 Pro2dImportAllSheets 实证，
#                   表现为该导出无声消失，链接不报错）。
#   R2 .ps1       : 含任何非 ASCII 字节（中文注释/字符串）时必须带 UTF-8 BOM。
#                   Windows PowerShell 5.1 按系统代码页（GBK 936）解码无 BOM 脚本，
#                   多字节序列错位 → brace/quote 配对乱 → 假阳性 parse error
#                   （2026-06-25 launch.ps1 等 3 文件 24 处实证）。
#   R3 .cmd/.bat  : 只许 ASCII。cmd.exe 936 代码页对 UTF-8 注释不友好。
#
# 白名单：构建产物目录（bin/obj/out/deploy/artifacts/cmake/.vs）不扫。
# 挂接：ci-gate.ps1 G3b 门（退出码 15）。也可手动跑。
#
# 本脚本自身含中文注释，必须保持 UTF-8 + BOM（否则 R2 会拦下它自己——这是特性）。

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

# 排除目录段（相对路径任意层级命中即跳过）
$excludeSegments = @('\bin\', '\obj\', '\out\', '\deploy\', '\artifacts\', '\cmake\', '\.vs\', '\node_modules\')

function Test-IsExcluded([string]$fullPath) {
  $rel = '\' + $fullPath.Substring($root.Length).TrimStart('\', '/') + '\'
  foreach ($seg in $excludeSegments) {
    if ($rel.ToLowerInvariant().Contains($seg)) { return $true }
  }
  return $false
}

# 返回文件里第一个非 ASCII 字节的 1-based 行号；全 ASCII 返回 0
function Get-FirstNonAsciiLine([byte[]]$bytes) {
  $line = 1
  foreach ($b in $bytes) {
    if ($b -eq 0x0A) { $line++; continue }
    if ($b -ge 0x80) { return $line }
  }
  return 0
}

function Test-HasUtf8Bom([byte[]]$bytes) {
  return ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
}

$hits = New-Object System.Collections.Generic.List[string]

# R1: .def 全 ASCII（BOM 也算非 ASCII 字节，一并拦截——.def 不该有 BOM）
foreach ($f in Get-ChildItem -Path $root -Recurse -File -Filter '*.def' -ErrorAction SilentlyContinue) {
  if (Test-IsExcluded $f.FullName) { continue }
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  $line = Get-FirstNonAsciiLine $bytes
  if ($line -gt 0) {
    $hits.Add(("[def-ascii] {0}:{1}: non-ASCII byte in .def (MSVC ANSI parser can silently swallow the next symbol line)" -f $f.FullName, $line))
  }
}

# R2: .ps1 含非 ASCII 必须带 UTF-8 BOM
foreach ($f in Get-ChildItem -Path $root -Recurse -File -Filter '*.ps1' -ErrorAction SilentlyContinue) {
  if (Test-IsExcluded $f.FullName) { continue }
  $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
  $line = Get-FirstNonAsciiLine $bytes
  if ($line -gt 0 -and -not (Test-HasUtf8Bom $bytes)) {
    $hits.Add(("[ps1-bom] {0}:{1}: non-ASCII content without UTF-8 BOM (PS 5.1 decodes as GBK -> phantom parse errors)" -f $f.FullName, $line))
  }
}

# R3: .cmd / .bat 全 ASCII
foreach ($ext in @('*.cmd', '*.bat')) {
  foreach ($f in Get-ChildItem -Path $root -Recurse -File -Filter $ext -ErrorAction SilentlyContinue) {
    if (Test-IsExcluded $f.FullName) { continue }
    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $line = Get-FirstNonAsciiLine $bytes
    if ($line -gt 0) {
      $hits.Add(("[cmd-ascii] {0}:{1}: non-ASCII byte in cmd/bat (codepage 936 mangles UTF-8)" -f $f.FullName, $line))
    }
  }
}

if ($hits.Count -eq 0) {
  Write-Host '[lint-encoding] OK - .def ASCII / .ps1 BOM / .cmd ASCII all clean.'
  exit 0
}

Write-Host ''
Write-Host ("[lint-encoding] FAIL - {0} hit(s):" -f $hits.Count)
foreach ($h in $hits) { Write-Host $h }
Write-Host ''
Write-Host 'Fix guide:'
Write-Host '  .def      -> rewrite comment in ASCII English (never white-list; the risk is silent symbol loss).'
Write-Host '  .ps1      -> add UTF-8 BOM: [IO.File]::WriteAllText($f,[IO.File]::ReadAllText($f,[Text.UTF8Encoding]::new($false)),[Text.UTF8Encoding]::new($true))'
Write-Host '  .cmd/.bat -> rewrite comment/text in ASCII English.'
exit 1
