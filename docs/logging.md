# 日志与诊断契约

本文是 managed 结构化日志的字段 schema、语义与配置契约。event 名清单见
[event-catalog.md](./event-catalog.md)；宿主加载/部署相关配置见
[creo-host-loading.md](./creo-host-loading.md) §4.3。

## 轨道总览

| 轨道 | 写入者 | 载体 | 形态 |
|---|---|---|---|
| managed 主日志 | `CreoLog`（L2 门面） | Serilog File sink | 自定义 JSONL（默认）或文本（`text`） |
| MEL / 三方桥 | `CreoLogBridgeSink` | 汇入同一 File sink | 结构化属性映射为同一 schema |
| native bootstrap | `CreoToolkit.NativeHost.dll` | 纯 Win32 JSONL | OTel 形（`timeUnixNano` / `traceId` / `severityNumber` …） |
| SDK telemetry | `CreoSdkLog` → `ActivitySource` | 无落盘 | `domain.action` Activity + `ctk.*` tag |
| 资源收支 | `CtkResourceGate` | 内存 `Interlocked` 计数 | 仅显式 `Report()` |
| 诊断包 | `DiagnosticBundle` | zip | 汇聚以上文件 + env + 模块版本 |

## managed JSONL schema

每行一个 JSON 对象，字段顺序固定（`JsonlFormatter`）：

| # | 字段 | 类型 | 说明 |
|---|---|---|---|
| 1 | `ts` | string | ISO-8601 **UTC**，毫秒精度，带 `Z`，例 `2026-09-16T12:34:56.789Z`。 |
| 2 | `level` | string | `trace` / `info` / `warn` / `error`（小写）。 |
| 3 | `layer` | string | `L1` / `L2` / `L3` / `L4` / `app`。 |
| 4 | `module` | string \| null | 逻辑模块名；property 缺省时填 `sdk`。 |
| 5 | `event` | string \| null | 结构化 event 键（见 event-catalog）；缺省时填空串。 |
| 6 | `msg` | string | 已渲染的消息；中文不转义。 |
| 7 | `loc` | string | `文件:行号` 或空串。 |
| 8 | `props` | object \| null | 业务上下文（仅一层对象）。 |
| 9 | `corr` | string \| null | 关联 ID，来自 `CreoLog.Scope(corr)`。 |
| 10 | `tid` | number | `Environment.CurrentManagedThreadId`。 |
| 11 | `err` | object \| null | 异常/错误对象，见下。 |

> **absent vs null**：`module` / `event` 即便为 `null` 也会显式写出（区分"字段不存在"与
> "值为空"）。`loc` / `corr` 缺省为 `""` / `null`。

### `err` 对象

Exception 路径（`CreoLog.Error(msg, ex, ...)`）：

```json
{ "code": -2147024891, "type": "System.IO.IOException", "msg": "...", "stack": "...", "inner": { ... } }
```

`code` 为 `Exception.HResult`；`inner` 递归同形，无内层异常时省略。`CreoLog.WithError(ex)`
产出相同形状，可直接作为 `props` 或 `err` 传入。

## 级别

`CreoLogLevel`（数值越大越详细，阈值取 `<=`）：

| 名称 | 数值 | Serilog |
|---|---|---|
| `Error` | 0 | `Error` |
| `Warn` | 1 | `Warning` |
| `Info` | 2 | `Information` |
| `Trace` | 3 | `Verbose` |

无 `Fatal` / `Debug`；`Fatal` 归入 `error`。运行期写 `CreoLog.Level` 立即生效（文件 logger
与 MEL 桥共用 `LoggingLevelSwitch`）。

## layer 语义

`layer` 默认由 `[CallerFilePath]` 路径前缀自动推导，无需手填：

| 路径片段 | layer |
|---|---|
| `CreoToolkit.NativeAbi` / `CreoToolkit.NativeHost` | `L1` |
| `CreoToolkit.Interop` | `L2` |
| `CreoToolkit.Sdk` | `L3` |
| `CreoToolkit.Agent` | `L4` |
| `CreoToolkit.App` / `CreoToolkit.Host` / `samples` | `app` |
| `tests` | `L2` |

显式传 `layer:` 可覆盖（桥接/测试场景）。

## 配置与环境变量

| 变量 | 作用 | 默认 |
|---|---|---|
| `CTK_ENV` | 基线 | 未设置 = Info + json |
| `CTK_DOTNET_LOG` | 总开关 | `on`（`off` / `0` 关闭） |
| `CTK_DOTNET_LOG_LEVEL` | 阈值 | 随 `CTK_ENV` |
| `CTK_DOTNET_LOG_FORMAT` | 文件格式 | 随 `CTK_ENV` |
| `CTK_DOTNET_LOG_FILE` | 非 Host 消费者 base path | `logs/dotnet/creo`（相对 CWD） |
| `CTK_DOTNET_LOG_RETENTION_DAYS` | 保留天数 | `14`（`<=0` 不清理） |
| `CTK_HOST_MANAGED_LOG` | Host base path | `AppContext.BaseDirectory/logs/host-managed.log` |

基线：`development` = Trace + both；`production` = Warn + json。

格式语义：
- `text` → 人类可读**文本文件**（无 JSONL，无 console）
- `json` → **JSONL 文件**
- `both` → **JSONL 文件** + **stderr 文本**

优先级：`CTK_DOTNET_LOG_*` > 旧 `CTK_LOG_*`（每个旧名触发一次 deprecated WARN）。
`CreoLog.SetFile` 显式调用优先于 `CTK_DOTNET_LOG_FILE`，故 Host 恒由 `CTK_HOST_MANAGED_LOG` 决定。

## 文件命名与保留

- managed：`<base>-<yyyyMMdd-HHmmss>-<pid>.log`，进程内唯一；单文件超过 **32 MB** 追加
  `_001` 等序号。`RetentionDays` 内按 mtime 清理同 stem 文件。
- 测试专用 `CreoLog.ResetForTest` 使用字面路径 + `FileShare.ReadWrite` sink。
- native bootstrap：`native-bootstrap-<iso>-p<pid>-<traceId32>.jsonl`，每 session 一份；
  `latest-session.pointer` 原子指向当前文件；`CTK_BOOTSTRAP_LOG_RETENTION_DAYS` 控制保留。

## native ↔ managed 关联

native 在 `user_initialize` 生成 W3C `traceId`，写入 bootstrap JSONL 与
`CTK_BOOTSTRAP_TRACE_ID` 环境变量。managed `session.start` 记录的
`props.bootstrapTraceId` 即该值，可作为两条轨道的 join key。managed 每条记录另带
`corr`（命令级）与 `tid`，但不重复携带 traceId。

## 约束

- 日志写入不得抛异常；sink/文件失败一律静默降级，不得影响业务路径。
- `msg` 按字面量渲染：字符串属性**不加引号**（Serilog 4 默认 `Render` 会加引号，
  `JsonlFormatter.RenderMessage` 已统一去掉）；消息内含 `{token}` 也按普通文本输出，不会被替换。
- 结构化 `props` 仅做一层对象序列化；嵌套/数组退化为字符串。
- 路径脱敏只在 `DiagnosticBundle` 生成 zip 时进行，日志文件本身保留原始内容。
