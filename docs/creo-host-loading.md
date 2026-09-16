# Creo Toolkit 宿主加载链路

> 本文记录把 .NET 装进 Creo 进程的运行时加载契约：加载了哪些 DLL、谁负责导出/调用、P/Invoke 最终落到哪里。
> 目标形态是 **DLL 合并、模块不合并**：运行时只有一个 native CreoToolkit DLL；源码和职责仍分成 Host 与 L1 ABI 两块。

> 架构决策记录参见本目录 [architecture-overview.md](architecture-overview.md)。

## 1. 运行时加载了哪些 DLL

| # | DLL | 类型 | 谁加载 | 关键职责 |
|---|---|---|---|---|
| 1 | **CreoToolkit.NativeHost.dll** | **native / CreoToolkit** | Creo 经 `protk.dat` -> `LoadLibrary` | 三位一体：TOOLKIT 入口、CLR 4（mscoree）宿主、L1 C ABI carrier。导出 `user_initialize`/`user_terminate` + Ctk_* C ABI（实际清单见 §3）+ `CreoToolkit_Free`/`Ctk_FreeBuffer`。链接 `protk_dllmd_NU.lib`/`ucore.lib`/`udata.lib`/`mscoree.lib`。 |
| 2 | **clr.dll（CLR 4）** | 系统 / .NET Framework | NativeHost 经 `CLRCreateInstance` / `ICLRRuntimeHost::Start` | 进程内 desktop CLR `v4.0.30319`。已在跑则附加，不 `Stop()`。 |
| 3 | **CreoToolkit.Host.dll** | .NET Framework / 托管宿主入口 | `ExecuteInDefaultAppDomain` | `public static int Method(string)` 入口：`Initialize`、`InitializeAndAttach`、`InitializeApp`、`Terminate`。`InitializeAndSmoke` 已由 `InitializeApp` + `samples/Modules/CreoToolkit.Samples.HostProbe` (`ctk.probe.smoke.model`) 替代。 |
| 4 | **CreoToolkit.Diagnostics.dll** | .NET / 可选启动诊断 | Host（仅诊断被激活时） | 模型预载/打开、启动诊断命令、ribbon 诊断和失败策略；不包含 unmanaged 入口或 app 生命周期所有权。 |
| 5 | **CreoToolkit.Sdk.dll** | .NET / L3 对象模型 | CLR 4 默认 AppDomain | `CreoSession`、模型/参数/选择/特征对象模型、主线程释放门、资源计数。 |
| 6 | **CreoToolkit.Interop.dll** | .NET / L2 接缝 | CLR 4 默认 AppDomain | `NativeMethods` P/Invoke、镜像结构体、`CtkAbi.Verify`。P/Invoke 库名是 `CreoToolkit.NativeHost`（已加载模块）。 |

一句话：**运行时真正触达 Creo 的 native DLL 只有 `CreoToolkit.NativeHost.dll`**。`CreoToolkit.NativeAbi` 只是托管侧 ABI 逻辑名，不代表需要部署或加载同名 sidecar DLL。

## 2. 模块边界与核心依赖关系

虽然运行时 DLL 合并，源码和心智模型仍保持分层：

| 模块 | 源码位置 | 职责 |
|---|---|---|
| NativeHost | `src/CreoToolkit.NativeHost/` | Creo TOOLKIT 入口、mscoree/CLR 4 启动、环境变量桥接、托管入口调用。 |
| L1 ABI | `src/CreoToolkit.NativeAbi/` | `Ctk_*` C ABI、内存释放门、日志、Creo `Pro*` 操作封装。 |
| Managed Host | `src/CreoToolkit.Host/` | 托管入口、启动参数编排、默认 AppDomain `LoadFrom` + `AssemblyResolve` 与 `ICreoApplication` 生命周期。 |
| Diagnostics | `src/CreoToolkit.Diagnostics/` | 可选启动诊断、目标模型预载/打开、诊断命令和 ribbon 诊断；不得反向依赖 Host。 |

`build_native.cmd CreoToolkit.NativeHost Release` 会把 L1 ABI 源文件直接编译链接进 `CreoToolkit.NativeHost.dll`，但不把 L1 的源码所有权迁到 NativeHost。

### 2.1 核心运行时依赖图

```text
Creo / protk.dat
  -> CreoToolkit.NativeHost.dll
       |- NativeHost: user_initialize / user_terminate / mscoree
       `- NativeAbi: Ctk_* ABI / Creo Pro* 调用（源码链接进同一 DLL）
  -> CLRCreateInstance → ICLRRuntimeHost::Start (v4.0.30319)
       -> ExecuteInDefaultAppDomain(CreoToolkit.Host.Bootstrap.*)
            |- CreoToolkit.Diagnostics.dll        [按环境配置激活]
            |- CreoToolkit.App.dll / Sdk.dll / Interop.dll / Polyfills.dll
            `- 外部 ICreoApplication              [默认 AppDomain, LoadFrom]
                 |- sample / 客户 app 主程序集
                 |- app 私有 Module 与 UI 依赖（AssemblyResolve 共享 Host/Sdk/Interop/Serilog）
                 `- 可选 Agent / Agent.Client

托管 P/Invoke 回程：
CreoToolkit.Interop -> DllImport("CreoToolkit.NativeHost")
  -> 进程内已 LoadLibrary 的 NativeHost.dll 内 Ctk_* / Pro*
```

这张图包含两种不同的“加载”：

1. **宿主加载**：Creo 装入 NativeHost，NativeHost 再通过 mscoree 启动/附加 CLR 4 并 `ExecuteInDefaultAppDomain` 调 `CreoToolkit.Host.dll`。
2. **业务应用加载**：Managed Host 在 CLR 已启动后，把一个 `ICreoApplication` 及其私有依赖 `LoadFrom` 进**默认 AppDomain**（`AssemblyResolve` 把 Host/Sdk/Interop/Serilog 钉回已加载副本）。

不部署 `nethost.dll` / `runtimeconfig.json`。一进程一套 CLR 4；不要在同一 xtop 里再混装 CoreCLR。

### 2.2 托管项目依赖与所有权

| 项目 | 直接项目依赖 | 主要所有权 | 是否在主加载链 |
|---|---|---|---|
| `CreoToolkit.Interop` | 无 | P/Invoke、ABI 镜像、回调注册、native handle、当前日志实现 | 是 |
| `CreoToolkit.Sdk` | `Interop` | `CreoSession` 与模型/特征/选择等对象模型，主线程和资源生命周期 | 是 |
| `CreoToolkit.App` | `Sdk`、`Interop` | `ICreoApplication`、声明式 builder、命令派发、菜单与 lifecycle | 是 |
| `CreoToolkit.Diagnostics` | `App`、`Sdk`、`Interop` | 可选模型准备、诊断命令/ribbon 与诊断失败策略 | 按配置加载 |
| `CreoToolkit.Host` | `Diagnostics`、`Sdk`、`App`、`Interop`；Serilog package | `public static int Method(string)` 入口、`LoadFrom` + `AssemblyResolve`、启动/终止编排 | 是 |
| `CreoToolkit.Agent.Client` | 无 | 命名管道 wire contract 与客户端 | 否，按 app 需要加载 |
| `CreoToolkit.Agent` | `Sdk`、`Interop`、`Agent.Client` | Agent 路由、handle、策略、管道服务 | 否，仅 Agent app/module 使用 |

`CreoToolkit.Diagnostics` 没有对 Host 的反向引用。Host 只经 `StartupDiagnostics.PrepareSession` / `RunAppActions` 两个同步入口调用它；`CTK_DIAGNOSTICS_ENABLED=0` 完全绕过，旧的模型/命令/ribbon 环境变量仍自动激活，`CTK_DIAGNOSTICS_REQUIRED=1` 才把可选诊断失败升级为启动失败。日志引擎所有权仍位于 Interop，本次拆分不扩大到日志基础设施迁移。

### 2.3 默认 AppDomain 边界

Framework 没有 collectible `AssemblyLoadContext`。插件与 Host/Sdk/Interop 同在 **默认 AppDomain**：

| 域 | 驻留内容 | 生命周期 |
|---|---|---|
| 默认 AppDomain | Host、可选 Diagnostics、App、Sdk、Interop、Serilog、外部 `ICreoApplication` | Creo 进程级；不 `Stop()` CLR 4，也不建子 AppDomain |

`CreoApplicationLoader` 用 `Assembly.LoadFrom` 装 app，并用 `AssemblyResolve` 把 Host/Sdk/Interop/Serilog 解析回已加载副本，避免两份 `ICreoApplication` / `CreoSession`。`Terminate` 处置已有 `_appHost` / session，不卸载程序集。

### 2.4 sample 聚合关系

```text
CreoToolkit.Samples.ProtkAppls.Core
  |- CreoToolkit.App
  `- 24 个 samples/Modules 项目
       `- 通常依赖 CreoToolkit.Sdk + CreoToolkit.App

CreoToolkit.Samples.ProtkAppls.WinForms
  |- ProtkAppls.Core
  |- Shell.WinForms
  `- Shell.Wpf
```

`samples/SampleCatalog.props` 是唯一人工维护真源：24 个 `CtkSampleModule` item 同时生成 Core 的 `ProjectReference` 与 `obj/.../SampleModuleCatalog.g.cs` 注册调用；2 个 `CtkSampleApp` item 同时驱动 Stage app 发布和 `app.json`。`Order` 元数据锁定现有 Core/WinForms 命令与菜单顺序。构建前会检查重复 ID/order/type、缺失项目和非法 manifest 字段，生成的类型/注册签名再由 C# 编译器校验；默认路径不做运行时目录扫描。

### 2.5 当前解决方案范围

`CreoToolkitRefactor.slnx` 收录 runtime / samples / 本仓 `tests/` 门禁（Host/Sdk/NetFxProbeApp）以及两个 native VCX 入口。全部托管 TFM 为 `net472`。

### 2.6 Build → Stage → Deploy

三个阶段现在具有独立目录与命令：

| 阶段 | 命令 | 输入 | 输出 / 副作用 |
|---|---|---|---|
| Build | `dotnet build CreoToolkitRefactor.slnx -c Release`；native 单独用 `build_native.cmd CreoToolkit.NativeHost Release v140` | 源码、SDK/toolchain | 仅各项目标准 `bin/obj` 与 NativeHost `out` 构建产物；不写 `deploy/`。 |
| Stage | `scripts/stage.ps1 -Configuration Release`；加 `-BuildNative` 构建 native；CI 可提供 `-NativeInputDir` | 默认 build/publish managed；native 可由本机构建或 CI 预置；另读取 `SampleCatalog.props` 与 launcher 模板 | `artifacts/stage/Release`，含 native、managed、apps、动态生成的可移植 `protk.host.dat`、app manifest 和 SHA256 `stage-manifest.json`；不要求 Creo 安装目录。不部署 `nethost.dll` / Host `runtimeconfig.json`。 |
| Deploy | `scripts/deploy.ps1 -StageRoot artifacts/stage/Release -Destination <目标目录>` | 已验证 Stage | 先校验 manifest/hash，再复制到指定环境；不触发 build。 |

`scripts/publish-deploy.ps1` 仅保留为兼容入口，内部顺序调用 Stage 和 Deploy，不再维护复制清单。Framework 宿主不读取 `runtimeconfig.json`。

## 3. NativeHost.dll 调用/导出明细

> **Note**：ctk_log 已停用。本节列表中的 `ctk_log.cpp` 已从 NativeHost 编译入口移除，仅作历史记录。

**编译进 NativeHost 的源**

- `src/CreoToolkit.NativeHost/src/host_entry.cpp`
- `src/CreoToolkit.NativeHost/src/clr_host_loader.cpp`
- `src/CreoToolkit.NativeHost/src/toolkit_touch.cpp`
- `src/CreoToolkit.NativeAbi/src/ctk_buffer.cpp`
- `src/CreoToolkit.NativeAbi/src/ctk_handle.cpp`
- `src/CreoToolkit.NativeAbi/src/ctk_log.cpp`
- `src/CreoToolkit.NativeAbi/src/ctk_ops.cpp`

构建时定义 `CTK_OPS_NO_ENTRY_STUBS`，屏蔽 `ctk_ops.cpp` 的独立 smoke stub，避免与 NativeHost 的真实 `user_initialize`/`user_terminate` 冲突。

**调用的 `Pro*`**

仅举常用域作示意：触达入口（`ProToolkitApplTextPathGet`、`ProMdlnameRetrieve`）/ 模型 / 图层 / 参数 / 特征 / 选择 / 窗口。完整清单按域 grep `src/CreoToolkit.NativeAbi/src/ctk_ops_*.cpp` 与生成绑定 `Generated/<版本>/Methods/Pro*.g.cs`。

**导出的符号**

- TOOLKIT 入口：`user_initialize` / `user_terminate`。
- L1 C ABI：`Ctk_GetAbiInfo` + 命令桥 4 个（`Ctk_CommandBridgeInitialize/Terminate/CommandActionAdd/LastCommandStatusGet`）+ Selection G3 LIVE 4 个 + Window LIVE 2 个 + 三态内存对（`Ctk_AllocBuffer`/`Ctk_FreeBuffer`、`Ctk_AllocOwnedArrayOfHandle`/`CreoToolkit_Free`）。完整清单以 `src/CreoToolkit.NativeAbi/exports.def` 与 `dumpbin /exports` 为准。
- 已移除（不再导出）：所有 `Ctk_Model*` / `Ctk_Layer*` / `Ctk_Parameter*` / `Ctk_FirstFeature` / `Ctk_FeatureElemtreeExtract` / `Ctk_ElemtreeFree` / `Ctk_SelectOwnedCopies` / `Ctk_SelectionHighlight` / `Ctk_SelectionUnhighlight` / `Ctk_SelectionDisplay`——这些 wrapper 改由 L3 直绑头文件。

## 4. 时序与链路

对应流程图（drawio 六页：总览 / Native 返回码 / Bootstrap / LoadFrom / Run / 停止）：
[diagrams/host-loading.md](./diagrams/host-loading.md) ·
[diagrams/host-loading.drawio](./diagrams/host-loading.drawio)。

```text
protk.dat -> LoadLibrary(CreoToolkit.NativeHost.dll) -> DllMain
  -> user_initialize [Creo 主线程]
      -> ctk_toolkit_touch()
      -> publish_native_host_path()          SetDllDirectory + CTK_HOST_NATIVE_DLL
      -> invoke_managed()
          -> CLRCreateInstance / ICLRRuntimeHost::Start (v4.0.30319)
          -> ExecuteInDefaultAppDomain(CreoToolkit.Host.Bootstrap.*)
              -> CreoSession.Attach
              -> CtkAbi.Verify               首个真实 P/Invoke
              -> [可选] Diagnostics.PrepareSession
              -> CreoApplicationLoader (LoadFrom) + CreoAppHost.Run
              -> [可选] Diagnostics.RunAppActions
```

默认 `InitializeApp` 与终止顺序未变；变化是 CLR 宿主从 hostfxr 换成 mscoree，插件从 collectible ALC 换成默认 AppDomain。

仓库中的 `protk.host.dat` 是无机器路径模板；正式 launcher、手工 launcher 和 R5 smoke 都必须在写入工作目录前物化 `exec_file` 与 `text_dir`，避免源码携带某台开发机的绝对路径。

`CTK_HOST_METHOD` 未设置时默认调用 `InitializeApp`。显式值只允许 `Initialize`、`InitializeAndAttach`、`InitializeApp`；未知值在 native 侧返回 `-13`，不会再静默回落到空入口。

托管到 native 的实际路径：

```text
NativeMethods.Ctk_*
  -> DllImport("CreoToolkit.NativeHost")
  -> 进程内已 LoadLibrary 的 NativeHost.dll
  -> NativeHost 内部 L1 ABI
  -> Creo Pro*
```

### 4.1 托管应用装配

`InitializeApp` 默认加载内置 `NoOpApp`（空命令集，仅闭环 attach/lifecycle），`DemoApp` 已移除迁出 runtime；命令演示走 `samples/` 项目，需要时通过环境变量加载：

| 环境变量 | 含义 |
|---|---|
| `CTK_APP_ASSEMBLY` | 外部 .NET 应用 DLL 路径。相对路径按 `AppContext.BaseDirectory` 解析。 |
| `CTK_APP_TYPE` | DLL 中实现 `CreoToolkit.App.ICreoApplication` 的完整类型名。 |
| `CTK_APP_ALLOWED_ROOTS` | App DLL 额外允许根目录，`;` 分隔。默认仅允许 `AppContext.BaseDirectory`；deploy launcher 自动填 app 目录。 |
| `CTK_APP_MSG_FILE` | Creo message 文件名，最多 39 个 ASCII 字符；Host 默认 `ctkdemo.txt`，sample launcher 设置为 `protk_samples.txt`。 |
| `CTK_DIAGNOSTICS_ENABLED` | `0/off` 强制关闭；`1/on` 显式启用；未设置时由旧诊断环境变量自动激活。 |
| `CTK_DIAGNOSTICS_REQUIRED` | `1/on` 时 Diagnostics 缺失或诊断失败会使 Host 启动失败；默认可选。 |
| `CTK_APP_PRELOAD_MODEL` | 由 managed Diagnostics 打开会话内已加载模型；不是 native 预载。 |
| `CTK_APP_LOAD_MODEL_PATH` | 由 managed Diagnostics 从文件路径加载模型。 |
| `CTK_APP_RUN_COMMAND` | 初始化完成后按名走与用户点击相同的 dispatch/handler 路径；未设置时等待用户交互。 |
| `CTK_APP_LOAD_RIBBON` / `CTK_APP_RIBBON_FILE` | 启用诊断 Ribbon 加载；文件默认 `ctk_demo.rbn`，加载失败记录日志但默认不抛。 |
| `CTK_APP_MODEL_NAME` / `CTK_APP_MODEL_TYPE` | sample 目标模型名与类型；类型按 sample 自身规则解析。 |
| `CTK_APP_SYSMENU_EXTRA` | 补充允许引用的 Creo 系统菜单名，`;` 分隔。 |
| `CTK_DISPATCH_ECHO` | 命令完成后向 message bar 回显；默认开启，`0/off/false/no` 关闭。 |
| `CTK_MESSAGEBAR_MIRROR` | 将业务 message bar 输出镜像到 managed log；默认关闭。 |

两者必须同时设置。未设置时行为为内置 `NoOpApp`（注册 0 条命令，主要用于纯 attach 场景）。

示例：

```bat
set CTK_APP_ASSEMBLY=<repo>\samples\CreoToolkit.Samples.ProtkAppls.WinForms\bin\Debug\net472\CreoToolkit.Samples.ProtkAppls.WinForms.dll
set CTK_APP_TYPE=CreoToolkit.Samples.ProtkAppls.ProtkSimpleSamplesApp
set CTK_APP_MSG_FILE=protk_samples.txt
```

当前这是“最小装配入口”：程序集 `LoadFrom` 进默认 AppDomain，`CreoToolkit.App`/`CreoToolkit.Sdk`/`CreoToolkit.Host` 等公共契约经 `AssemblyResolve` 复用宿主已加载实例。不承诺同进程热重载。app manifest 只声明 `type`、`assembly` 和可选 `msgFile`。

`ICreoApplication` 生命周期顺序为：

1. `Initialize(builder)`：仅声明命令、菜单和 Ribbon；此时不要持有短生命周期 native 资源。
2. `CreoAppHost.Run` 注册命令和菜单成功后调用 `OnInitialize(ctx)`；可在此订阅事件或启动 app 级资源。
3. 仅当 `OnInitialize` 完整返回后，停止阶段才调用配对的 `OnTerminate(ctx)`。
4. `OnTerminate` 在 `BridgeTerminate` 与 `CreoSession.Dispose` 之前执行，session 仍可用；异常会记录但不阻断后续清理。

命令回调不会让 managed 异常穿过 native 边界：成功返回 0，handler 异常返回 1，命令名/token 未注册返回 2。
默认成功回显 `[ctk] done: <command>`；可用 `CTK_DISPATCH_ECHO=0` 关闭。

该路径已在真实 Creo 中验证过：`CTK_APP_ASSEMBLY` 指向 `samples/CreoToolkit.Samples.ProtkAppls.WinForms`。当前 24 个模块及顺序以 `samples/SampleCatalog.props` 为准；命令元数据以 `SampleCommandMetadata.All` 为准；handler 实现位于各 `samples/Modules/CreoToolkit.Samples.*/*Registration.cs`。

### 4.2 命令、菜单与 Ribbon 注册顺序

`CreoAppHost.Run` 按以下固定顺序消费 `CreoAppBuilder` 快照：

1. app `Initialize(builder)` 声明普通命令、option command、菜单和 Ribbon。
2. `BridgeInitialize` 钉住单一 native dispatch callback。
3. 普通命令 `CommandActionAdd` + `CommandDesignate`；这是硬失败阶段，任一失败使初始化失败。
4. 菜单外观按顶级菜单 → 子菜单 →普通按钮 → option command → check/radio → Ribbon 顺序注册。外观类失败记录 WARN 并尽量继续，不撤销已注册命令。
5. 注册成功后调用 `OnInitialize`。

WinForms sample 的 `CreoToolkit` 顶级菜单只有 7 个主题 dialog launcher；109 条模块普通命令在 dialog 内按元数据分组运行，合计 116 条普通命令元数据。`ExtCreoMenu` 另外演示嵌套菜单、4 项 radio group 和 1 项 check button，对应的 2 个 option command 不计入 116 条普通命令元数据。

`CreoAppBuilder.LoadCustomRibbon(path)` 和诊断变量 `CTK_APP_LOAD_RIBBON` 最终都调用 `ProRibbonDefinitionfileLoad`。当前 ExtCreoMenu sample 明确跳过二进制 `.rbn` 资产，因此默认 sample 不能被描述为“已安装自定义 Ribbon”；只有提供并成功加载实际 `.rbn` 文件时才成立。

### 4.3 .NET 日志配置

| 环境变量 | 默认 / 说明 |
|---|---|
| `CTK_ENV` | 日志基线：`development` = Trace + both；`production` = Warn + json；未设置 = Info + json。 |
| `CTK_DOTNET_LOG` | .NET 日志总开关（`off` / `0` 关闭，其余开启）。 |
| `CTK_DOTNET_LOG_LEVEL` | `error` / `warn` / `info` / `trace`；运行期改 `CreoLog.Level` 通过 `LoggingLevelSwitch` 立即生效。 |
| `CTK_DOTNET_LOG_FORMAT` | `text`=人类可读文本文件；`json`=JSONL 文件；`both`=JSONL 文件 + stderr 文本。 |
| `CTK_DOTNET_LOG_FILE` | 非 Host 消费者（SDK/Agent）的日志 base path；Host 启动时由 `CTK_HOST_MANAGED_LOG` 显式接管。 |
| `CTK_DOTNET_LOG_RETENTION_DAYS` | managed 日志保留天数，默认 14；`<=0` 不清理。 |
| `CTK_HOST_MANAGED_LOG` | Host managed log base path；未设置时为 `AppContext.BaseDirectory/logs/host-managed.log`。 |

managed 实际文件名为 `<base>-<yyyyMMdd-HHmmss>-<pid>.log`；单文件超过 32 MB 后追加 `_001` 等
序号。目录内按 stem 保留，`DiagnosticBundle` 采集最新一份为 zip 内 `logs/host-managed.log`。
字段 schema、级别/layer 语义与 native↔managed 关联方式见 [logging.md](./logging.md)。

旧 `CTK_LOG*` 名称只作迁移期 fallback，并产生 deprecated WARN；新配置不得继续写旧名称。

## 5. 历史 sidecar 的处理

早期曾用 `CreoToolkit.Native.Creo.dll` 作为独立 L1 sidecar，让托管层直接 P/Invoke 它。真实 Creo 验证发现：未由 Creo 注册加载的 sidecar 在 `user_initialize` 阶段调用部分 `Pro*` 有挂起风险。当前 `CreoToolkit.NativeAbi` 是 ABI 源码目录和托管 P/Invoke 逻辑名，不代表同名 DLL 需要部署。

因此当前契约是：

- hosted 运行不部署、不加载 L1 sidecar。
- 历史 sidecar 名称 `CreoToolkit.Native.Creo.dll` 不再由当前脚本生成；若在工作区看到它，应视为陈旧构建残留并删除。
- `build_core.cmd` / `build_ops.cmd` 已移除；探针产物 `CreoToolkit.NativeAbi.{CoreTest,OpsProbe}.dll` 现统一由 `build_native.cmd <target> Release` 编译。
- `build_creo.cmd` / `build_host.cmd` 薄包装已移除;Creo link probe 走 `build_native.cmd CreoToolkit.NativeAbi.CreoLinkProbe Release`,NativeHost full build 走 `build_native.cmd CreoToolkit.NativeHost Release`。
- `out/managed` 中不应出现任何 `CreoToolkit.Native*.dll`；若存在，应视为陈旧构建残留。

## 6. Native 产物命名规则

| 产物 | 性质 | 是否 hosted 运行加载 |
|---|---|---|
| `CreoToolkit.NativeHost.dll` | 唯一运行时 native DLL | 是 |
| `CreoToolkit.NativeAbi.CoreTest.dll` | 脱 Creo core 测试产物 | 否 |
| `CreoToolkit.NativeAbi.OpsProbe.dll` | L1 ops + Creo lib 链接探针 | 否 |
| `CreoToolkit.NativeAbi.CreoLinkProbe.dll` | 最小 Creo Toolkit 链接探针 | 否 |

## 7. Visual Studio 项目规则

`.slnx` 包含运行时宿主的 native Makefile 项目：

| VS 项目 | 性质 | 构建入口 |
|---|---|---|
| `CreoToolkit.NativeHost.vcxproj` | 运行时宿主 DLL | `build_native.cmd CreoToolkit.NativeHost $(Configuration)` |
| `CreoToolkit.NativeAbi.Probes.vcxproj` | L1 测试/探针（内部测试载体，不随发行版分发） | `build_native.cmd native-probes $(Configuration)` |

这些项目用于 Visual Studio / VS MSBuild 下查看和构建 native 代码。

底层 native 构建已统一到根目录 `CMakeLists.txt` / `build_native.cmd`。这些 `.vcxproj` 只作为 Visual Studio 入口，
不会再维护第二套 cl/link 命令；目录内旧 `build_*.cmd` 仅作为人工短命令保留。常用目标：

```bat
build_native.cmd native-all Release
build_native.cmd CreoToolkit.NativeHost Release
build_native.cmd native-probes Release
```

## 8. 已解决问题

| 项 | 处理 |
|---|---|
| hosted 运行依赖 sidecar | 已取消。NativeHost 直接链接 L1 ABI 源码，托管 resolver 绑定到 NativeHost。 |
| `CTK_LOG_FILE` 惰性 open 关闭 `ctk_log_set_file` 已打开 sink | 已修复。显式 `ctk_log_set_file` 后不再被未设置的环境变量覆盖。（注：ctk_log 已于 v4.1 停用。） |
| 预载模型空 handle 返回成功 | 已消解。2026-07-10 native 预载链（`preload_model_if_requested`）整体移除，模型加载全走 managed `CreoToolkit.Diagnostics.StartupDiagnostics`。 |
| adopted model handle 生命周期 | 已补 `Ctk_ModelClearAdoptedHandle`，`CreoSession.Dispose()` 经主线程 dispatcher 清理借用缓存。 |
| CLR 4 已在进程中时 `Start()` | 附加，不视为失败；不 `Stop()`。 |
| `ctk_toolkit_touch` 使用弱语义转换 API | 已修复。改用 `ProToolkitApplTextPathGet`，同时验证 `text_dir` 可由 Creo 取回。 |

## 9. 停止 / 卸载策略

当前策略（停止时处置已有 host/session，不卸载 CLR，不承诺同进程 reload）：

- 外部 `ICreoApplication` 用 `Assembly.LoadFrom` 进默认 AppDomain；`AssemblyResolve` 共享 Host/Sdk/Interop/Serilog。
- `CreoAppHost.Dispose` 末尾清 `_cmds` 作纵深防御。
- `user_terminate` 触发链：托管 `Terminate -> DisposeManagedHost -> CreoSession.Dispose` 释放 L3/L1 tracked 资源 + adopted handle。程序集留在默认域。
- **NativeHost.dll 本身不主动 `FreeLibrary`**，也不 `ICLRRuntimeHost::Stop()`。`DLL_PROCESS_DETACH` 不做文件 IO；停止证据写入 session JSONL/ETW，并通过 active/latest pointer 维护会话状态。旧 `host-marker-*.log` / `host-native-*.log` 已停用。
- native command action 当前没有完整 unregister/re-register 契约，因此支持边界仍是“单进程单 app 生命周期”，不是热重载插件平台。

### 9.1 CreoAppHost.Run 失败点回滚矩阵

| 失败点 | 已分配资源 | 是否调 `OnTerminate` | 回滚顺序 |
|---|---|---|---|
| F1 `_app.Initialize` 抛 | 无 | 否 | `DisposeManagedHost` → `session.Dispose` |
| F2 `BridgeInitialize` 失败 | `_registry` / `_pin` 已建 | 否 | `BridgeTerminate` → `pin` → `registry` → `session` |
| F3 第 N 条 `CommandActionAdd` 失败 | 前 N-1 条已进 Creo 路由表 | 否 | 同 F2 |
| F4 `MenubarPushbuttonAdd` 失败 | 命令全成功，菜单半注册 | 否 | 同 F2（Creo 菜单 UI 残留由重启清） |
| F5 `OnInitialize` 抛 | bridge + 命令 + 菜单全成功 | 否（`_lifecycleInitialized` 不置位） | `Dispose` 跳 `OnTerminate` → `BridgeTerminate` → `pin` / `registry` / `session` |

**关键契约**：F5 之前 `OnInitialize` 内自己产生的应用侧副作用（订阅 / worker / 连接）必须由 app 在 throw 前回滚——框架不补调 `OnTerminate`，与 .NET 构造器异常语义一致。

### 9.2 停止时处置顺序（无程序集卸载）

Framework 默认 AppDomain 没有 collectible ALC。`DisposeManagedHost` 只处置已构造对象，**不**卸载程序集：

```
1. appHost.Dispose()   -> OnTerminate（若已成功 OnInitialize）+ BridgeTerminate + _pin.Dispose
2. session.Dispose()   -> CallbackRegistry / 句柄 / 主线程释放门
3. 静态字段置 null     -> Bootstrap 不再持有 host/session
```

同进程不承诺再 `LoadFrom` 另一个 app（`InitializeApp` 重入返回 -1）。要换 app 就重启 Creo。

## 10. 仍需保留的开放风险

| 项 | 位置 | 说明 |
|---|---|---|
| `CTK_HOST_*` 进程级环境变量冲突 | `host_entry.cpp` / `Bootstrap.cs` | 多实例会互相覆盖路径与日志（预载 handle 已随 2026-07-10 预载链移除）。当前策略是不支持多实例，后续需要实例后缀或单宿主插件化。 |
| Creo 停止 app 是否 `FreeLibrary` native DLL | 真实 Creo | 当前 detach 分支在 loader lock 下不做 IO，因此仅靠现有日志不能证明 `FreeLibrary`；NativeHost 仍按 process-lifetime 设计。若要验证需增加 loader-lock 安全的外部观测机制。 |
