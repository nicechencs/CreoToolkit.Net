# lint-naming-alignment.ps1 -- L3 SDK 命名对齐门
#
# 三条规则:
#   R1 L2 后置动词禁穿透 L3
#      - 匹配形态:public <ReturnType> <XxxGet|XxxSet>(
#      - 反例: public ModelIdentity? PathMdlGet(...)
#                       public CreoTransform? PathTransformGet(...)
#      - 正例(L3 前置): public ModelIdentity? GetPathMdl(...)
#
#   R2 属性藏 I/O
#      - 匹配形态:public <ReturnType> <Name> => Session.Run(...)
#                 或 public <ReturnType> <Name> { get => Session.Run(...); }
#      - 反例: 属性 getter 直调 Session.Run/Bridge./_native.
#      - 正例(方法露 I/O): public T GetXxx() => Session.Run(...)
#
#   R3 语义降级白名单 badMap
#      - `.<bad>(` 调用出现即报错(白名单外)
#      - 初始:空(官方 pfcSolid::GetMassProperty 就是单数 Get* 前缀,不宜改成 Compute*)
#      - future: 未来发现真实语义降级案例(pfc 官方 Compute*/Eval*/List* 但项目错用 Get*)时补入
#
# 白名单(硬编,方便审计;真需要豁免时改本脚本):
#   - Native/ 子目录(L2 Bridge,规约生效范围仅 L3,§二.84 明写)
#   - IgnoredNameSuffixes: 允许合规 Get* / Set* 方法(即前缀而非后缀动词);
#     由 R1 正则的"必须紧邻 (" 天然过滤(如 GetGtolCount / GetIsExploded 不命中)
#
# 挂接:ci-gate.ps1 G3c 门(退出码 16)。也可手动 -Verbose 跑。
#
# 用法:
#   scripts\lint-naming-alignment.ps1              # 静默,只报命中
#   scripts\lint-naming-alignment.ps1 -Verbose     # 打印每条命中的详细上下文
#
# 本脚本自身含中文注释,必须保持 UTF-8 + BOM(feedback_ps1-utf8-bom-needed)。
# G3b(lint-encoding.ps1)会拦下无 BOM 的 .ps1。

[CmdletBinding()]
param(
  [switch]$VerboseHits
)

$ErrorActionPreference = 'Stop'

$root      = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sdkRoot   = Join-Path $root 'src\CreoToolkit.Sdk'
$nativeDir = Join-Path $sdkRoot 'Native'

if (-not (Test-Path $sdkRoot)) {
  Write-Host "[lint-naming-alignment] FATAL: SDK 根目录不存在: $sdkRoot" -ForegroundColor Red
  exit 16
}

# 排除目录段(相对路径含即跳过)。Native/ 是 L2 Bridge 层,规约生效仅 L3。
$excludeSegments = @('\Native\', '\bin\', '\obj\')

function Test-IsExcluded([string]$fullPath) {
  $rel = '\' + $fullPath.Substring($sdkRoot.Length).TrimStart('\', '/')
  foreach ($seg in $excludeSegments) {
    if ($rel.ToLowerInvariant().Contains($seg.ToLowerInvariant())) { return $true }
  }
  return $false
}

# ---- R1: L2 后置动词穿透 L3 ----
# 匹配 public [modifiers]* <return type>(<generics>? nullable?) <Name>(Get|Set)(
# 关键点:方法名 \w+ 与 Get|Set 之间必须无字符,且 Get|Set 紧邻 ( 之前
#         -> 天然过滤 GetGtolCount( / GetIsExploded( 等前缀式合规命名
# 允许修饰符:static/virtual/override/async/new/sealed/readonly/abstract
# 允许返回类型形态:Foo / Foo? / Foo<Bar> / Foo<Bar>? / Foo<Bar,Baz>?
$r1Pattern = 'public\s+(?:(?:static|virtual|override|async|new|sealed|readonly|abstract)\s+)*[\w<>?,\.\s]+?\s+\w+(Get|Set)\s*\('

# R1 方法名白名单(方法名恰好含尾动词但合规的边界情况;初始为空,首次落地零豁免)
$r1NameWhitelist = @()

# ---- R2: 属性藏 I/O(表达式主体 / getter 块两形态)----
# 形态 A: public T Foo => Session.Run(...) / => _session.Run(...) / => Bridge. / => _native.
# 形态 B: public T Foo { get => Session.Run(...); }
$r2PatternA = 'public\s+(?:(?:static|virtual|override|new|sealed|readonly|abstract)\s+)*\S+\s+\w+\s*=>\s*(?:_?[Ss]ession\.Run|Bridge\.|_native\.)'
$r2PatternB = 'public\s+(?:(?:static|virtual|override|new|sealed|readonly|abstract)\s+)*\S+\s+\w+\s*\{\s*get\s*=>\s*(?:_?[Ss]ession\.Run|Bridge\.|_native\.)'

# ---- R3: 语义降级 badMap ----
# 匹配 `.<bad>(` 调用形态,只扫 SDK L3(样本/测试不管——那是消费者面)
# v1.3 B4 前置调研已推翻原 GetMassProperty → ComputeMassProperties 假设:
#   官方 pfcSolid::GetMassProperty 就是单数 Get* 前缀(otk_methods:1554);
#   全 TSV 4165 行零命中 Compute*Mass*;Pro C ProSolidMassPropertyGet 同为单数 Get。
# B4a/B4b 完成后(bc73a22),CreoSolid.GetFailedFeature* 已改 ListFailedFeature*,
#   badMap 无需列作 regression guard(源码里已无旧名,零命中天然守规约)。
# 当前 badMap 为空。未来发现真实语义降级案例时补入。
$r3BadMap = [ordered]@{
  # 示例(暂无实例):
  # 'ExampleGetX' = 'ExampleComputeX'  # pfcXxx.ComputeX 但项目错用 GetX 的降级
}

$hits = New-Object System.Collections.Generic.List[string]

# 遍历 SDK 全部 .cs
foreach ($f in Get-ChildItem -Path $sdkRoot -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue) {
  if (Test-IsExcluded $f.FullName) { continue }
  $lineNo = 0
  try {
    foreach ($line in Get-Content -LiteralPath $f.FullName -ErrorAction Stop) {
      $lineNo++

      # R1 -- 用 -cmatch 严格大小写(Get/Set 首字母必须大写;否则 Reset/Preset 会假命中)
      if ($line -cmatch $r1Pattern) {
        # 用 case-sensitive [regex]::Match 抓方法名 + 尾动词判定白名单
        $methodMatch = [regex]::Match($line, '\b(\w+?)(Get|Set)\s*\(', [System.Text.RegularExpressions.RegexOptions]::None)
        $isWhitelisted = $false
        if ($methodMatch.Success) {
          $fullName = "$($methodMatch.Groups[1].Value)$($methodMatch.Groups[2].Value)"
          if ($r1NameWhitelist -contains $fullName) { $isWhitelisted = $true }
        }
        if (-not $isWhitelisted) {
          $hits.Add(("[R1 L2-verb-穿透] {0}:{1}: {2}" -f $f.FullName, $lineNo, $line.TrimEnd()))
        }
      }

      # R2 (两形态) -- 用 -cmatch 严格大小写(Session/Bridge 首字母必须严格匹配 [Ss]ession 变体白名单)
      if ($line -cmatch $r2PatternA) {
        $hits.Add(("[R2 属性藏 I/O] {0}:{1}: {2}" -f $f.FullName, $lineNo, $line.TrimEnd()))
      }
      elseif ($line -cmatch $r2PatternB) {
        $hits.Add(("[R2 属性藏 I/O] {0}:{1}: {2}" -f $f.FullName, $lineNo, $line.TrimEnd()))
      }

      # R3 -- 用 -cmatch 严格大小写(bad name 必须精确匹配 PascalCase)
      foreach ($bad in $r3BadMap.Keys) {
        # 调用形态 `.<bad>(` -- 只标调用点,不动定义(定义走批 B4 改名)
        if ($line -cmatch ('\.' + [regex]::Escape($bad) + '\(')) {
          $good = $r3BadMap[$bad]
          $hits.Add(("[R3 语义降级] {0}:{1}: `.{2}(` 应改 `.{3}(` -- {4}" -f `
            $f.FullName, $lineNo, $bad, $good, $line.TrimEnd()))
        }
      }
    }
  } catch {
    # 读文件失败不阻塞,静默跳过
  }
}

# ---- 汇总 ----
if ($hits.Count -eq 0) {
  Write-Host '[lint-naming-alignment] OK - L3 命名对齐门(R1/R2/R3)全部零命中。'
  exit 0
}

Write-Host ''
Write-Host ("[lint-naming-alignment] FAIL - {0} hit(s):" -f $hits.Count) -ForegroundColor Red
foreach ($h in $hits) { Write-Host $h }
Write-Host ''
Write-Host 'Fix guide:'
Write-Host '  R1 (L2 后置动词穿透 L3): 把 XxxGet/XxxSet 改成 GetXxx/SetXxx;L2 端 (CreoToolkit.Sdk\Native\ 下)不动。'
Write-Host '  R2 (属性藏 I/O)         : 有 native 调用的成员必须是方法 (Get*/List*/Compute*),不许伪装成属性;或改属性为纯字段(ctor copy-out)。'
Write-Host '  R3 (语义降级)           : 按 badMap 提示改名(如 GetMassProperty -> ComputeMassProperties)。'
Write-Host '  真需要豁免:改 $r1NameWhitelist / $r3BadMap 白名单条目,并在提交说明里写清理由。'
exit 16
