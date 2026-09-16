# 将托管运行时从 .NET 8（CoreCLR）改为 .NET Framework 4.x 的影响分析

> **快照，不是当前状态。** 本文写于迁移前，描述的是当时的 CoreCLR/nethost 产品模型与改动面。
> 代码已按本文落地为 **net472 + mscoree CLR 4**；现行契约见
> [architecture-overview.md](architecture-overview.md) 与 [creo-host-loading.md](creo-host-loading.md)。
>
> 分析日期：2026-09-16（第二轮按「整仓改为 net472」收口并补遗漏）  
> 分析对象：当时的 `release-creo4` 树快照。  
> 目标假设：**整仓替换**为 .NET Framework 4.7.2（`net472`）。不再保留 net8 目标，也不做双运行时。

---

## 1. 结论先行

这不是「把 `TargetFramework` 从 `net8.0` 改成 `net472`」的工程。当前产品的进程模型是：

```text
Creo (xtop.exe)
  → LoadLibrary(CreoToolkit.NativeHost.dll)
    → nethost.dll → hostfxr.dll → CoreCLR
      → [UnmanagedCallersOnly] CreoToolkit.Host.Bootstrap
        → NativeLibrary.SetDllImportResolver
        → collectible AssemblyLoadContext 装载客户 App
```

.NET Framework 4.x 使用完全另一套 CLR 宿主（`mscoree` / `ICLRMetaHost` / `ICLRRuntimeHost`），没有 `hostfxr`、`runtimeconfig.json`、`UnmanagedCallersOnly`、`NativeLibrary`、`AssemblyLoadContext`。

| 判断 | 说明 |
|---|---|
| 只改 csproj、保留 nethost | **不能启动**。先编译失败，即便打桩也会在 `hostfxr_initialize_for_runtime_config` 失败。 |
| 工程文件数量 | **35** 个 SDK 风格 csproj；其中 **34** 个目前只打 `net8.0` / `net8.0-windows`。Interop 已是 `net8.0;net472`。 |
| 手写 C# | **约 291** 个文件（另有 **496** 个 Generated `.g.cs`，生成器已按 Framework 可编译的 `DllImport` 输出）。 |
| 真正阻断 | NativeHost 宿主重写；Host 入口 ABI；P/Invoke 逻辑名解析；App 装载隔离；**Sdk `ICreoNative` 约 50 个 default interface methods（CS8701）**。 |
| 语言层 | `Directory.Build.props` 已设 `LangVersion=latest`。file-scoped namespace / record / nullable **不必回退到 C# 7.3**。DIM 是 CLR 限制，与语言版本无关。 |
| 工作量量级 | Native 宿主：人周级。托管 TFM + BCL 补丁 + DIM 拆除：人周到人月。产品级（隔离语义、绑定重定向、真机、文档、测试）：**人月**。 |
| 决策 | **整仓改为 net472**。Interop 已有的 net8 TFM 一并去掉。Host/NativeHost 必须同步换成 CLR 4 宿主，不能只改 csproj。 |

仓库里写的是 **.NET 8**，不是历史意义上的 .NET Core 3.1。下文「CoreCLR / net8」与用户口中的「.NET Core」是同一条产品线。

---

## 2. 当前基线（必须先看清）

### 2.1 项目与 TFM

| TFM | 数量 | 项目 |
|---|---:|---|
| `net8.0` | 29 | Sdk、App、Agent、Agent.Client、ProtkAppls.Core、24 个 Module |
| `net8.0-windows` | 5 | Host、Diagnostics、ProtkAppls.WinForms、Shell.WinForms、Shell.Wpf |
| `net8.0;net472` | 1 | **仅** `CreoToolkit.Interop` |
| 合计 csproj | 35 | 全部 `Sdk="Microsoft.NET.Sdk"` |

无 `global.json`、无 `nuget.config`、无 `Directory.Packages.props`。解决方案是 `CreoToolkitRefactor.slnx`（dotnet CLI / VS 的 XML solution，注释按 **dotnet 8+** 使用）。

公共默认（`Directory.Build.props`）：

- `LangVersion=latest`
- `Nullable=enable`
- `ImplicitUsings=enable`
- `Platforms=x64`
- `AppendTargetFrameworkToOutputPath=true`

### 2.2 已经为 net472 做过的事（Interop）

`src/CreoToolkit.Interop/CreoToolkit.Interop.csproj` 是仓库里唯一的双目标项目：

- TFM：`net8.0;net472`
- net472 额外包：`System.Memory 4.6.0`、`System.Diagnostics.DiagnosticSource 8.0.1`、`System.Text.Json 8.0.5`
- `_Polyfills/IsExternalInit.cs`（`#if !NET5_0_OR_GREATER`）
- `StringMarshal.ReadFixedBuffer` 在 `NET472` 下走 `char[]` 中转（Framework 没有 `string(ReadOnlySpan<char>)`）
- `CreoLog` 对 `Environment.ProcessId` 做 `#if NET6_0_OR_GREATER`
- 生成器 `.targets` 已有多 TFM 竞态防护（outer build 先生成一次）
- `ICommandBridge` / `IMenuBridge` **故意不用 default interface methods**（net472 CLR 不支持）

**下游全部只消费 Interop 的 net8.0 面。** 把 Sdk/App/Host 改成 net472 才会真正吃到 Interop 的 net472 产物。

### 2.3 运行时加载链（现状）

详见 [creo-host-loading.md](./creo-host-loading.md)。与本迁移直接相关的事实：

1. Creo 只 `LoadLibrary` **一颗** native DLL：`CreoToolkit.NativeHost.dll`。
2. NativeHost 用同目录 `nethost.dll` 找 `hostfxr`，读 `CreoToolkit.Host.runtimeconfig.json`，启动 **CoreCLR**。
3. 托管入口是 `[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]` 的 `Initialize` / `InitializeAndAttach` / `InitializeApp` / `Terminate`。hostfxr 以 `UNMANAGEDCALLERSONLY_METHOD` 取函数指针。
4. `DllImport("CreoToolkit.NativeAbi")` 是**逻辑名**，磁盘上没有这个 DLL。Host 用 `NativeLibrary.SetDllImportResolver` 绑到 `CTK_HOST_NATIVE_DLL`（即 NativeHost.dll）。这是因为部署布局是 `native/` 与 `managed/` 分目录。
5. 外部 App 进 **collectible `AssemblyLoadContext`**；Host / App / Sdk / Interop / Serilog 留在 default ALC，避免类型身份分裂。
6. Stage 会**删除** app 的 `*.runtimeconfig.json`。进程内只允许 Host 那一份 runtimeconfig 选定运行时。

当前 Host runtimeconfig（`deploy/managed/CreoToolkit.Host.runtimeconfig.json`）：

```json
"tfm": "net8.0",
"frameworks": [
  { "name": "Microsoft.NETCore.App", "version": "8.0.0" },
  { "name": "Microsoft.WindowsDesktop.App", "version": "8.0.0" }
]
```

客户机硬依赖 **x64 .NET 8 Desktop Runtime**。

---

## 3. 改动面总表

按「必须动 / 大面积机械改 / 可保留」分层。数字是本树当时的扫描结果。

| 层 | 必须改的文件/工程（约） | 性质 | 不改的后果 |
|---|---|---|---|
| Native 宿主 | `host_entry.cpp`、`dotnet_host_loader.cpp/.h`、`CMakeLists.txt`、`build-native-v140.ps1`、`host_probe.cpp` | **重写** | 无法把 Framework CLR 装进 xtop |
| CMake / 部署脚本 | `CMakeLists.txt`、`stage.ps1`、`launch.ps1`、`publish-deploy.ps1` | 删 nethost / runtimeconfig，改校验清单 | Stage/Launch 继续要求 CoreCLR 产物 |
| Host 入口与装载 | `Bootstrap.cs`、`CreoApplicationLoader.cs`、`LoadedApplication.cs`、`NativeBootstrapHandoff.cs`、Host.csproj | **重写** | 编译不过；P/Invoke 找不到 NativeAbi；App 无法按现契约卸载 |
| Sdk `ICreoNative` DIM | `ICreoNative.cs` 约 50 个默认实现 + 私有 fake | **拆除** | Sdk 对 net472 报 CS8701 |
| 其余 34 个 csproj 的 TFM | 全部 `net8.0` / `net8.0-windows`；Interop 去掉 net8 | 机械但连带 RID / EnableDynamicLoading / UseWindowsForms | 无法被 CLR 4 加载 |
| BCL 补丁 | ~90+ 手写文件用 `ThrowIf*`；Agent 管道；`IReadOnlySet`；`FrozenDictionary`；STJ 包 | 机械 + 少量 API 替换 | 编译失败 |
| NuGet | Interop 已有的 STJ/Memory 要扩散到 App/Agent/Sdk；Serilog 绑定重定向 | 包图 + `app.config`/`AssemblyResolve` | 运行期 `FileNotFoundException` / 版本冲突 |
| WinForms / WPF | 5 个 `net8.0-windows` 工程；WPF-UI 4.0.2 | TFM + DPI manifest；Mica 需真机确认 | UI sample 不能编或视觉回退 |
| Generated 496 文件 | 生成器本身基本不用改 | 保持 `DllImport` | — |
| 文档 | README、architecture、host-loading、plugin-runtime-guide.html、NativeHost README、THIRD_PARTY_NOTICES | 全文替换加载故事 | 对外契约与实现不一致 |
| NativeAbi C ABI | `ctk_*.cpp` / `exports.def` | **基本不动** | — |

粗算「会碰到的托管源文件」：手写 291 个里，绝大多数因为 file-scoped namespace / `ThrowIf*` / record 至少要能在 net472 + 现代 SDK 下编过。其中真正要改逻辑的是 Host（~8 文件）+ `ICreoNative` DIM + Agent 管道（含 `BoundedLineReader`）+ 若干 Marshal/诊断 polyfill；其余可用共享 polyfill + 包引用消化。

---

## 4. P0 阻断项（不解决则产品不能跑）

### 4.1 NativeHost：从 hostfxr 换成 CLR 4 宿主

涉及：

- `src/CreoToolkit.NativeHost/src/host_entry.cpp`  
  `load_assembly_and_get_function_pointer`、`UNMANAGEDCALLERSONLY_METHOD`、`kRuntimeConfig`、`invoke_managed`
- `src/CreoToolkit.NativeHost/src/dotnet_host_loader.cpp` / `.h`  
  整文件都是 `nethost` / `get_hostfxr_path` / 三个 hostfxr 导出
- `CMakeLists.txt`  
  强制 `DOTNET_HOST_PACK` 含 `nethost.h`、`nethost.dll`、`hostfxr.h`、`coreclr_delegates.h`，POST_BUILD 复制 `nethost.dll`
- `scripts/build-native-v140.ps1` 传入 `-DDOTNET_HOST_PACK`

NativeHost **并不链接** `nethost.lib`，运行时 `LoadLibrary` 同目录 `nethost.dll`。Framework 路径应对称为：

```text
user_initialize
  → LoadLibrary(mscoree.dll)
  → CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost)
  → GetRuntime(L"v4.0.30319")
  → GetInterface(CLSID_CLRRuntimeHost, ICLRRuntimeHost)
  → Start()          // 若进程里已有 CLR 4，则附加而非二次 Start
  → ExecuteInDefaultAppDomain(
        CreoToolkit.Host.dll,
        L"CreoToolkit.Host.Bootstrap",
        L"InitializeApp",
        L"" / 环境块,
        &ret)
user_terminate
  → ExecuteInDefaultAppDomain(..., L"Terminate", ...)
  → 不要 Stop() CLR（与今天「不卸载 CoreCLR」一致）
```

约束：

- `ExecuteInDefaultAppDomain` 要求托管方法签名为 **`public static int Method(string pwzArgument)`**，不是今天的 `int __cdecl ()`。
- 不要把 NativeHost 改成 C++/CLI 混编。Creo 4 生产 native 必须 **VC140 + 动态 CRT**，DllMain 里混托管是经典死锁坑。继续保持纯 native 宿主。
- 一进程只能有一套 CLR 4。若其它 TOOLKIT 插件已经启动 v4，必须附加（语义类似今天 hostfxr 的 rc=1/2）。
- **禁止**在同一 xtop 里再混装 CoreCLR。

`protk.dat` / `user_initialize` / `user_terminate` 对 Creo 的契约可以不变。NativeAbi 的 `Ctk_*` / `Pro*` 导出也不用为了换 CLR 而改。

### 4.2 Host 入口：去掉 `UnmanagedCallersOnly`

`src/CreoToolkit.Host/Bootstrap.cs` 四个入口全部是：

```csharp
[UnmanagedCallersOnly(EntryPoint = "InitializeApp", CallConvs = [typeof(CallConvCdecl)])]
public static int InitializeApp() { ... }
```

Framework CLR **没有** `UnmanagedCallersOnlyAttribute`，hostfxr 的 `UNMANAGEDCALLERSONLY_METHOD` 也不存在。必须改成 `ExecuteInDefaultAppDomain` 能调用的静态方法（带 `string` 参数），或改用 `ICorRuntimeHost` + `InvokeMember`（灵活但更复杂）。

`CallConvs = [typeof(CallConvCdecl)]` 还用了 C# 12 集合表达式；改签名后这条会一起消失。

### 4.3 P/Invoke 逻辑名：没有 `NativeLibrary.SetDllImportResolver`

今天：

```text
DllImport("CreoToolkit.NativeAbi")
  → Bootstrap.ResolveNativeLibrary
  → NativeLibrary.Load(CTK_HOST_NATIVE_DLL)
  → 已加载的 CreoToolkit.NativeHost.dll
```

`NativeLibrary` 是 net5+ API。Framework 的 P/Invoke 搜索顺序是：调用方程序集目录（`managed/`）→ System32 → PATH。NativeHost 却在 `native/`。

可选替换（必须三选一，否则第一次 `CtkAbi.Verify` 就是 `DllNotFoundException`）：

1. **部署侧**：把 NativeHost 复制/硬链为 `managed/CreoToolkit.NativeAbi.dll`（或改 DllImport 名为 `CreoToolkit.NativeHost` 并 `SetDllDirectory(nativeDir)`）。
2. **native 侧**：`user_initialize` 里 `SetDefaultDllDirectories` + `AddDllDirectory`，DllImport 改用真实文件名。
3. **托管侧**：手写 `LoadLibrary` + `GetProcAddress` 的薄封装（等于自造 resolver）。

`NativeBootstrapHandoff.AcknowledgeNative` 使用 `UnmanagedType.LPUTF8Str`（netstandard2.1 / netcoreapp2.1+，**不在 net472**）。要改成 `LPStr` + 显式 UTF-8 字节，或 native 改收 UTF-16。

### 4.4 App 装载：`AssemblyLoadContext` 没有 Framework 对等物

`CreoApplicationLoader.CreoApplicationLoadContext`：

- collectible ALC
- `LoadFromStream` 装主程序集
- `AssemblyDependencyResolver` 读 `.deps.json`
- 把 App/Sdk/Host/Interop/Serilog 解析回 default ALC

Framework 没有 ALC、没有 `AssemblyDependencyResolver`、没有 collectible unload。

| 方案 | 隔离 | 与 Creo 回调的兼容性 | 评价 |
|---|---|---|---|
| 全部 `Assembly.LoadFrom` 进默认 AppDomain + `AssemblyResolve` 共享契约程序集 | 弱，近似今天「共享 Host/Sdk/Interop」 | 好（同一类型身份，P/Invoke/GCHandle 安全） | **唯一现实方案** |
| 子 AppDomain | 强卸载 | **差**：`ICreoApplication` / `CreoSession` / native 回调不能跨 AD 当普通对象用，除非全部改 `MarshalByRefObject` | 不建议 |
| 放弃插件卸载 | 与今天「Terminate 只是请求 Unload」更接近 | 好 | 文档必须明确降级 |

结论：Framework 上应放弃 collectible 卸载承诺，改「默认 AD + LoadFrom + AssemblyResolve」。`LoadedApplication.Alc` 字段要么删除，要么变成永远 null 的兼容壳。

`EnableDynamicLoading=true`（Host、ProtkAppls.Core、ProtkAppls.WinForms）是 Core 插件发布开关，用来生成 runtimeconfig/deps。Framework 下无意义，应删，改 copy-local。

### 4.5 Sdk：`ICreoNative` 默认接口方法（CS8701）

Interop 已经用「`ICommandBridge` / `IMenuBridge` 拆接口」躲开 DIM，注释写明 *net472 不支持 default interface methods*。Sdk **没有**遵循同一约束。

`src/CreoToolkit.Sdk/Native/ICreoNative.cs` 上约 **50** 个成员带默认实现 `=> throw new NotImplementedException()`（单位、参数带单位、几何、图层、绘图等后加能力）。这是给测试 fake 免实现用的。

**即便 `LangVersion=latest`，对 `net472` 也是 CS8701。** 整仓改 TFM 后 Sdk 会直接编不过。必须三选一：

1. 把默认实现拆成独立 `ICreoNativeXxx` 扩展接口（与 Interop 的桥拆分同套路）；fake 不实现则 `as` 后跳过。
2. 抽 `CreoNativeDefaults` 抽象基类，fake / `CreoNativeBridge` 继承。
3. fake 补全全部成员（工作量大，且每加 API 都要改 fake）。

推荐 1 或 2。这是第一轮分析漏掉的 **P0 编译阻断**，与 NativeHost 重写同级。

`IGeneratedInvoker` 没有 DIM，只有 `ThrowIf*`。

---

## 5. P1：工程、BCL、包

### 5.1 34 个 csproj 的 TFM 怎么改

SDK 风格 **可以保留**，不必退回 packages.config / 旧 csproj。现代 .NET SDK 编 `net472` 是正路。

| 现在 | 改为 | 备注 |
|---|---|---|
| `net8.0` | `net472` | Sdk、App、Agent、Agent.Client、Core、24 Module |
| `net8.0-windows` | `net472` + `UseWindowsForms` / `UseWPF` | **不要**写成 `net472-windows` |
| Host 的 `RuntimeIdentifier=win-x64` `SelfContained=false` | 删；改 `PlatformTarget=x64` | Framework 没有 Core 那种 RID 发布模型 |
| Interop `net8.0;net472` | **只留 `net472`** | 条件包改为无条件引用（Memory / STJ / DiagnosticSource） |

CI 必须安装 **.NET Framework 4.7.2 Developer Pack**（或 NuGet 包 `Microsoft.NETFramework.ReferenceAssemblies.net472`）。Interop 注释已写：CI 没有 v4.7.2 引用程序集时只能丢掉 net472。

WinForms/WPF 工程继续 `UseWindowsForms` / `UseWPF`。SDK 会改用 `Microsoft.NET.Sdk.WindowsDesktop` 语义（可显式写 Sdk，也可靠 Use* 属性）。

### 5.2 语言特性：多数能留

因为 `LangVersion=latest`，**不要**把语言打回 C# 7.3。下列在 net472 + 现代编译器下可以编过（部分要 polyfill）：

| 特性 | 本仓库规模 | net472 策略 |
|---|---|---|
| file-scoped namespace | **783 / 787** 个 `.cs` | 保留 |
| `readonly record struct` | **32** 个文件，约 44 处类型 | 保留；`IsExternalInit` polyfill 要从 Interop 提升到全仓（或 `Directory.Build.props` 编译链接一个 polyfill 项目） |
| nullable 注解 | 全仓启用 | 保留；`[NotNullWhen]` 等需 `Nullable` / polyfill 包 |
| ImplicitUsings | 全仓 | SDK 对 net472 仍会生成一套 global usings |
| `nint` | Interop / App 菜单桥大量使用 | C# 9 关键字，net472 映射 `IntPtr`，保留 |
| switch 表达式 | 约 110 处 | 保留 |
| using 声明 | 约 33 个文件 | 保留 |
| 集合表达式 `[...]` | Host `CallConvs`、`SharedAssemblies` 等少数 | 保留或改成 `new[]`；不是阻断 |
| `required` + `init` | `CommandMetadata` 2 个 `required`；App/WPF 约 20+ `init` | 保留；需仓级 `RequiredMember` / `CompilerFeatureRequired` / `IsExternalInit` polyfill |
| `with { }` | Sdk 2 处（`CreoPlane`、`CreoNativeBridge.DimAttach`） | 随 record 保留 |
| Index/Range `[^n..]` | Shell.WinForms `ShellCommandBrowser.cs` 1 处 | 改 `Skip`/`Take`；net472 无 `System.Index`/`Range` |
| default interface methods | **Interop 有意 0 处；Sdk `ICreoNative` 约 50 处** | **必须拆除**（§4.5），不是「继续禁止」就能过编译 |
| `LibraryImport` / `delegate* unmanaged` | **0 处** | 生成器已走经典 DllImport |
| raw string `"""` | **0 处** | — |
| `IAsyncDisposable` / `await using` | **0 处** | — |

真正不能靠 LangVersion 解决的是 **CLR/BCL API**（下一节），不是语法。

### 5.3 必须 polyfill 或改写的 BCL API

扫描命中（手写代码，不含 Generated）：

| API | 引入版本 | 命中规模 | 改法 |
|---|---|---|---|
| `ArgumentNullException.ThrowIfNull` | net6 | **92** 个文件 | 共享 `ThrowHelper` 或 `PolySharp` / `CommunityToolkit` |
| `ArgumentException.ThrowIfNullOrWhiteSpace` / `ThrowIfNullOrEmpty` | net7 | **51** 个文件 | 同上 |
| `ObjectDisposedException.ThrowIf` | net7 | Sdk、Agent.Client 等 | 同上 |
| `IReadOnlySet<T>` | net5 | App `SystemMenuNames`、Agent router/policy 共 4 处 | 改 `ISet<T>` / `IReadOnlyCollection<T>`，或自备接口 |
| `StringSplitOptions.TrimEntries` | net5 | 3 处（App、Host loader、PtAsm） | 手写 Trim |
| `Path.TrimEndingDirectorySeparator` | netcore3/net5 | Host loader 1 处 | `TrimEnd('\\','/')` |
| `Environment.ProcessId` | net5 | Host Bootstrap、Interop CreoLog（后者已有 `#if`） | `Process.GetCurrentProcess().Id` |
| `Environment.ProcessPath` | net6 | App `DiagnosticBundle` | `Process.GetCurrentProcess().MainModule.FileName` |
| `OperatingSystem.IsWindows` | net5 | WinForms sample、AgentDemo | `RuntimeInformation.IsOSPlatform` 或直接假定 Windows |
| `HashCode.Combine` | netcore2.1 / 不在 net472 BCL | Sdk 5 个文件（`CreoColor` / `CreoDtlentity` / `CreoFeatureGroupContext` / `CreoUnit` / `CreoParamValue`） | 包 `Microsoft.Bcl.HashCode` |
| `NamedPipeServerStreamAcl.Create` | net5 | `NamedPipeAgentServer.CreatePipe` | Framework 用带 `PipeSecurity` 的旧构造函数 |
| `Stream.ReadAsync(Memory<byte>, CancellationToken)` | netcore2.1 | `NamedPipeAgentServer.BoundedLineReader` | net472 的 `Stream` 没有 Memory 重载；改 `ReadAsync(byte[], int, int, ct)` |
| `StreamReader.ReadLineAsync(CancellationToken)` | net7 | Agent.Client | 改无 CT 重载 + 取消用 `Wait`/`ReadAsync` 包装 |
| `StreamWriter.WriteLineAsync(ReadOnlyMemory<char>, CT)` | netcore2.1 形态不同 | Agent.Client | `WriteLineAsync(string)` |
| `ArgumentOutOfRangeException.ThrowIfNegative` / `ThrowIfNegativeOrZero` | net8 | `CreoWindows`、`CreoParameters`、`FinalizerReleaseQueue` | 并入 ThrowHelper |
| `Math.Clamp` | netcore2.0 / 不在 net472 | `PtGeomRegistration.cs` 1 处 | 手写 min/max |
| `FrozenDictionary` / `ToFrozenDictionary` | net8 | `ProErrorPolicy.cs` | 改普通 `Dictionary` |
| `string.StartsWith(char)` | netcore2.1 | `DiagnosticBundle.cs` | 改 `StartsWith("/")` / `StartsWith("\\")` |
| `[NotNullWhen(true)]` | netcore3 注解 | `CreoCommandContextExtensions.cs` 4 处 | 仓级 nullable 属性 polyfill |
| `SupportedOSPlatform` | net5 | Agent 管道 5 处 | 可删或留作空属性 |
| `Span<T>` / `stackalloc Span` | netcore + System.Memory | Interop、Sdk Native 桥 | Interop 已引 `System.Memory`；Sdk 改 net472 必须同样引用 |
| `unmanaged` 泛型约束 | C# 7.3 | `ProArrayMarshal` | 可用 |

`System.Text.Json`：Interop 的 net472 已引用 8.0.5。下列工程在 net8 上吃 inbox STJ，改 net472 **必须加 PackageReference**：

- `CreoToolkit.Agent.Client`（`AgentWireCodec`）
- `CreoToolkit.Agent`（`AgentArgs`、`HandleId`）
- `CreoToolkit.App`（`DiagnosticBundle`）

传递依赖大致包括 `System.Text.Encodings.Web`、`System.Memory`、`System.Runtime.CompilerServices.Unsafe`、`Microsoft.Bcl.AsyncInterfaces`。

### 5.4 NuGet 兼容性

| 包 | 现版本 | net472 |
|---|---|---|
| Serilog | 4.0.0 | 支持 net462 / netstandard2.0。**4.x 不再钉程序集版本 2.0.0**，Framework 上几乎肯定要绑定重定向 |
| Serilog.Sinks.File | 6.0.0 | 支持 |
| Serilog.Sinks.Console | 6.0.0 | 支持 |
| System.Memory | 4.6.0 | 已在 Interop net472 使用 |
| System.Text.Json | 8.0.5 | 支持 net462（NuGet，非 inbox） |
| DiagnosticSource | 8.0.1 | 支持 net462 |
| WPF-UI | 4.0.2 | 4.x 线同时打 net462 与 net8.0-windows。Mica / WinRT 在 Framework 上可能降级，需真机确认 |

**进程内绑定重定向的坑：** Fusion 读的是 **`xtop.exe.config`**，不是 `CreoToolkit.Host.dll.config`。插件自己的 app.config **不会自动生效**。必须：

- 写 `AppDomain.CurrentDomain.AssemblyResolve`（与今天 ALC 共享列表是同一类问题），或
- 要求客户改 Creo 安装目录的 `xtop.exe.config`（运维上很差）。

推荐前者，并在 Host 启动时集中处理 Serilog / STJ / Memory / Unsafe。

未使用（对 Framework 是好事）：`Microsoft.Extensions.*`、`LibraryImport` 源生成器、CsWinRT 包。

---

## 6. P1：Agent / UI / 诊断

### 6.1 Agent 命名管道

管道类型本身在 net472 存在。要改的是 API 形态：

- 服务端 ACL：`NamedPipeServerStreamAcl.Create` → `new NamedPipeServerStream(..., pipeSecurity)`
- 服务端读行：`BoundedLineReader` 的 `Stream.ReadAsync(_chunk.AsMemory(), ct)` 在 net472 不存在
- 客户端异步：去掉 `ReadLineAsync(ct)` / `WriteLineAsync(memory, ct)`
- `IReadOnlySet<string>` 白名单
- 补 STJ 包

`PipeOptions.Asynchronous`、`PipeSecurity`、按当前用户 SID 收紧 ACL，在 Framework 上反而是更原生的 API。

### 6.2 WinForms

`samples/CreoToolkit.Samples.Shell.WinForms` 是自绘 Fluent（`Theme/Fluent*.cs`），P/Invoke `DwmSetWindowAttribute`，**没有**使用：

- `Application.SetHighDpiMode`
- `ApplicationConfiguration.Initialize`
- `Application.SetDefaultFont`
- WebView2

DPI 现在靠 `AutoScaleMode.Dpi`。迁到 Framework 应补 **app.manifest** 的 `dpiAware` / `dpiAwareness`。字体栈 `Segoe UI Variable` → `Segoe UI` 在 Framework 上可用。日志截断 `_logBox.Lines[^LogMaxLines..]` 要用 `Skip`/`Take` 改写。

Host 的 `UseWindowsForms=true` 是为了让 CoreCLR 带上 WindowsDesktop，从而能加载 WinForms/WPF 插件。Framework 上 WinForms 是 inbox，Host 仍建议显式 `UseWindowsForms`，避免插件加载 UI 框架时缺引用。

### 6.3 WPF

`Shell.Wpf` 用 WPF-UI 4.0.2 的 `FluentWindow` + `WindowBackdropType="Mica"`。包声明支持 net462，但 Mica 依赖较新的 DWM/WinRT，在 Creo 4 常见的 Win10 早期或 Win7 上会退化为普通窗口。需要一次真机烟测，而不是只看 NuGet TFM。

`net8.0-windows` → `net472` + `UseWPF` + `UseWindowsForms`（该工程两者都开了，因为要嵌 Win32 owner）。

### 6.4 Diagnostics

`CreoToolkit.Diagnostics` 只是 `net8.0-windows` 的薄辅助，没有独立的 Windows-only API。随 Host 改 TFM 即可。`ThrowIf*` 同样要 polyfill。

---

## 7. 部署、脚本、文档

### 7.1 产物布局对比

**现在（Stage 契约）**

```text
native/CreoToolkit.NativeHost.dll
native/nethost.dll                          ← Framework 删除
native/protk.host.dat
managed/CreoToolkit.Host.dll
managed/CreoToolkit.Host.runtimeconfig.json ← Framework 删除
managed/CreoToolkit.Host.deps.json          ← hostfxr 输入，Fusion 不用
managed/*.dll（App/Sdk/Interop/Diagnostics/Serilog）
apps/<id>/app.json + 业务 DLL               ← 继续保留；runtimeconfig 本来就会被删
```

**Framework 后**

```text
native/CreoToolkit.NativeHost.dll           ← 仍是唯一 Creo 入口
native/protk.host.dat
managed/CreoToolkit.Host.dll
managed/*.dll
managed/ 可能需要 NativeAbi 名字的 native 副本或依赖 SetDllDirectory
apps/<id>/...
```

客户机前置从「安装 x64 .NET 8 Desktop Runtime」变为「机器已有 .NET Framework 4.7.2/4.8」。Creo 4 年代的 Windows 通常已带 4.x，这是整仓迁移的主要商业理由。

### 7.2 必须改的脚本

| 文件 | 现假设 | 改什么 |
|---|---|---|
| `scripts/stage.ps1` | 校验 `nethost.dll`、`Host.runtimeconfig.json`；`dotnet publish -r win-x64 --self-contained false`；删 app runtimeconfig | 去掉 nethost/runtimeconfig 必选项；publish 不要 RID；拷贝 binding 策略或文档化 AssemblyResolve |
| `scripts/launch.ps1` | 缺 nethost / runtimeconfig 直接失败；设 `CTK_HOST_RUNTIME_CONFIG` | 改为校验 Host.dll + NativeHost.dll；可删该环境变量 |
| `scripts/publish-deploy.ps1` | 包一层 Stage | 随 Stage |
| `build-dotnet.cmd` | 「XML solution，dotnet 8+」 | 仍可用现代 SDK 编 net472；注释改掉 |
| `CMakeLists.txt` / `build-native-v140.ps1` | `DOTNET_HOST_PACK` 强制 | 删除 host pack 依赖 |

`EnableDynamicLoading` 从三个 csproj 删除后，Stage 不能再靠它产 deps.json。

### 7.3 必须改的文档

| 文件 | 原因 |
|---|---|
| `README.md` | 徽章、前置依赖、故障表（nethost / hostfxr / ALC unload） |
| `docs/architecture-overview.md` | 「地道的 .NET 8」+ hostfxr 图 |
| `docs/creo-host-loading.md` | 整篇加载契约 |
| `docs/creo-plugin-runtime-guide.html` | 客户可见的 nethost 步骤 |
| `src/CreoToolkit.NativeHost/README.md` | 运行时策略 |
| `THIRD_PARTY_NOTICES.md` | 「.NET 8 SDK and runtime」 |
| `docs/diagrams/class-diagram.md` | 已记录 DIM 限制，需补宿主模型 |
| `CONTRIBUTING.md` | SDK 版本表述 |

当时树内没有 `.github/` CI YAML。`InternalsVisibleTo` 已点名的测试工程同样要改 TFM，且大量断言 ALC / `UnmanagedCallersOnly` 的用例会失效。

---

## 8. 分层检查清单（按模块）

### L0 生成管线

- Python 生成器输出的是经典 `[DllImport]` + `IntPtr` + `unsafe struct`/`fixed`，**不是** net8 专用。
- `.targets` 已处理多 TFM。整仓改 net472 后 outer/inner 竞态防护仍可留。
- 本仓无 `tools/CreoToolkit.Generator` 源码；沿用已提交的 `Generated/Creo4_M140`。

### L1 NativeAbi

- **不因换 CLR 而改 ABI。**
- 仍编进 NativeHost.dll。
- `toolkit_touch.cpp` 只是注释提到 nethost。

### L2 Interop

- 已是工作量最小的一层。整仓替换时 **去掉 `net8.0` TFM**，Memory/STJ/DiagnosticSource 改为无条件引用。
- `ICommandBridge` / `IMenuBridge` 拆分、`RadioGroupItem` 不用 record，都是为 net472 预留的，可保留。
- `_Polyfills/IsExternalInit.cs` 目前是 `internal`，**必须提升为仓级共享**，否则 Sdk/App 的 record `init` 编不过。
- `UnmanagedType.LPUTF8Str` 只在 Host `NativeBootstrapHandoff`，不在 Interop。

### L3 Sdk

- **P0：拆除 `ICreoNative` 约 50 个 DIM**（§4.5）。
- `FrozenDictionary`（`ProErrorPolicy`）改为 `Dictionary`。
- `AllowUnsafeBlocks`、`Span`、`stackalloc`、record 几何类型：加 `System.Memory` + 仓级 `IsExternalInit` + `Microsoft.Bcl.HashCode`。
- `HashCode.Combine`：`CreoColor` / `CreoDtlentity` / `CreoFeatureGroupContext` / `CreoUnit` / `CreoParamValue`。
- `with { }`：`CreoPlane`、`CreoNativeBridge.DimAttach`（语言可留）。
- 大量 `ThrowIf*` / `ThrowIfNegative*` 要 polyfill。
- 没有 ALC / NativeLibrary。

### L4 App

- 声明式 builder、命令/菜单：逻辑可移植。
- `CommandMetadata` 的 `required`/`init`：仓级 polyfill。
- `SystemMenuNames` 的 `IReadOnlySet` + `TrimEntries`。
- `DiagnosticBundle`：STJ、`ProcessPath`、`StartsWith(char)`、`JavaScriptEncoder`（随 STJ 包）。
- `CreoCommandContextExtensions`：`[NotNullWhen]` polyfill。

### L4 Host / Diagnostics

- **本迁移的托管核心。** 见 §4。

### L4 Agent

- 见 §6.1。Client 零 Sdk 依赖，但依赖 STJ。

### Samples（28 个工程）

- 24 个 Module：几乎全是 `ThrowIfNull` + file-scoped namespace，TFM 一改 + polyfill 即可。
- AgentDemo：额外依赖 Agent，并调用 `OperatingSystem.IsWindows()`。
- PtGeom：`Math.Clamp`（net472 BCL 没有）。
- Core / WinForms 聚合宿主：`EnableDynamicLoading` 删除；WinForms 工程 `net8.0-windows` → `net472`。
- Shell.WinForms：`_logBox.Lines[^LogMaxLines..]` 必须改写。
- 文档示例路径 `bin\Debug\net8.0-windows\` 全部失效（`AppendTargetFrameworkToOutputPath=true` 会变成 `net472`）。

---

## 9. 「只改 csproj、保留 nethost」会怎样

按失败顺序：

1. **编译失败**：`UnmanagedCallersOnly`、`NativeLibrary`、`AssemblyLoadContext`、`AssemblyDependencyResolver`、`LPUTF8Str`、`ThrowIf*`、`IReadOnlySet`、`NamedPipeServerStreamAcl`、**`ICreoNative` DIM（CS8701）**、`FrozenDictionary`、`Stream.ReadAsync(Memory)`。
2. 即便全部打桩：`hostfxr_initialize_for_runtime_config` 只启动 CoreCLR。net472 没有一份能让 hostfxr 接受的 Desktop runtimeconfig → native 事件 `runtime_load_failed`，`invoke_managed` 返回 **-11**。
3. `UNMANAGEDCALLERSONLY_METHOD` 在 Framework 程序集上无对应导出 → **-12**。
4. 没有 resolver 时 `DllImport("CreoToolkit.NativeAbi")` → `DllNotFoundException`。
5. Stage/Launch 仍然硬性检查 `nethost.dll` 与 Host runtimeconfig。

所以：**TFM 替换与宿主替换必须同一 PR 序列交付，不能先改 csproj 再「以后改 native」。**

---

## 10. 工作量与风险

### 10.1 工作流拆分（若决定整仓替换）

| 序号 | 工作流 | 规模 | 依赖 |
|---|---|---|---|
| 1 | 共享 polyfill（ThrowHelper、IsExternalInit、Nullable 属性、HashCode）+ Directory.Build.props 统一 net472 包 | S | 无 |
| 2 | Interop 去掉 net8 TFM，Memory/STJ/DiagnosticSource 改为无条件引用 | S（已完成大半） | 1 |
| 3 | 拆除 `ICreoNative` DIM（拆接口或抽象基类）+ 更新 fake | M | 2 |
| 4 | Sdk / App / Agent / samples TFM + STJ/Memory/HashCode 包 | M | 1–3 |
| 5 | Agent 管道 API 替换（ACL 构造函数、`ReadAsync(Memory)`、ReadLine/WriteLine） | S–M | 4 |
| 6 | Native 宿主 mscoree 重写 + CMake | M | 可与 3–4 并行 |
| 7 | Host：入口签名、DllImport 解析、LoadFrom+AssemblyResolve | M | 6 的 ABI 约定 |
| 8 | Stage/Launch/部署布局；删除 `CTK_HOST_RUNTIME_CONFIG` / nethost | S | 6–7 |
| 9 | WinForms/WPF 真机 DPI/Mica；`[^n..]` 改写 | S | 4 |
| 10 | 文档与故障表（README、host-loading、plugin-guide、CONTRIBUTING） | S | 6–8 |
| 11 | 测试与 Creo 4 真机 e2e | M–L | 全部 |

Native 单点（工作流 6）大约数人日到一周（做过 CLR 4 宿主的人）。产品完成（含 DIM 拆除、隔离语义降级、xtop 绑定重定向、24 模块回归、测试、文档）是 **数周到数月**，取决于测试覆盖和 Creo 真机矩阵。

### 10.2 主要风险

1. **与其它 .NET 插件抢 CLR。** Framework 插件在 Creo 里很常见（含历史 Object TOOLKIT .NET）。必须处理「CLR 已启动」。
2. **绑定重定向落在 xtop.exe 上。** 漏做 AssemblyResolve 时，Serilog 4 / STJ 8 会在客户机随机炸。
3. **插件卸载语义消失。** 今天文档已写「Unload 只是请求」。Framework 连请求都没有。长期运行的 Creo 会话若反复加载不同 App，会积漏。
4. **Win7 / 早期 Win10。** 若迁移动机是老 OS：.NET 8 本来就不支持；Framework 4.7.2 在 Win7 需要离线安装包。WPF-UI Mica 在那些 OS 上无意义。
5. **VC140 宿主 + mscoree。** 可行且传统，但调试器、混合调用栈、STA 套间要在真机重新验证（Creo UI 线程近似 STA）。
6. **ALC / UnmanagedCallersOnly 相关测试。** `Host.Tests` 几乎肯定大量耦合 ALC / UnmanagedCallersOnly；`Sdk.Tests` 的 fake 依赖 `ICreoNative` DIM。这两块会把工作量往上抬。
7. **仓库内没有 `app.config`。** Fusion 默认读 `xtop.exe.config`。必须在 Host 启动时做 `AssemblyResolve`，不能假设 DLL 旁的 config 会生效。

---

## 11. 已定方案：整仓仅 net472

按产品决策，**整仓改为 .NET Framework 4.7.2**，不再保留 net8 目标，也不做「Host 留 net8、库双目标」。

| 不做 | 原因 |
|---|---|
| 只改 csproj、native 继续 nethost | 见 §9，不能启动 |
| Interop/Sdk 双目标、Host 仍 CoreCLR | 与「整仓」决策不符 |
| net8 + net472 双宿主 | 两套加载路径，维护成本接近两份产品 |
| 语言打回 C# 7.3 | 780+ 个 file-scoped 文件无意义重写 |

实施顺序按 §10.1 的 1→11。架构上必须同时接受：

- NativeHost 从 hostfxr 换成 `ICLRRuntimeHost`
- 放弃 collectible ALC 卸载
- 用 `AssemblyResolve` + `SetDllDirectory`/`LoadLibrary` 代替 `NativeLibrary` / `.deps.json`
- 拆除 `ICreoNative` DIM

**保持 `LangVersion=latest` 与 SDK 风格工程。**

---

## 12. 实施时不要漏的架构变更（检查单）

整仓替换不是 TFM 细节，下列必须写进变更说明，而不是事后补：

1. CLR 4 进程内宿主；`user_initialize` 若发现 CLR 已启动则附加。
2. 托管入口改为 `public static int Method(string)`。
3. P/Invoke 逻辑名 `CreoToolkit.NativeAbi` 的解析策略（复制 DLL / `SetDllDirectory` / 改 DllImport 名）。
4. 插件装载改为默认 AppDomain + `LoadFrom` + `AssemblyResolve`。
5. Serilog / STJ / Memory / Unsafe 的版本统一由 Host `AssemblyResolve` 钉死。
6. `ICreoNative` 扩展面拆接口或抽象基类，私有 fake 同步改。

---

## 13. 附录：关键路径索引

| 主题 | 路径 |
|---|---|
| 全仓语言/TFM 默认 | `Directory.Build.props` |
| Interop 双目标与 net472 包 | `src/CreoToolkit.Interop/CreoToolkit.Interop.csproj` |
| IsExternalInit | `src/CreoToolkit.Interop/_Polyfills/IsExternalInit.cs` |
| Span 字符串 polyfill | `src/CreoToolkit.Interop/Marshal/StringMarshal.cs` |
| 托管入口 | `src/CreoToolkit.Host/Bootstrap.cs` |
| App ALC | `src/CreoToolkit.Host/CreoApplicationLoader.cs` |
| Native 加载 | `src/CreoToolkit.NativeHost/src/host_entry.cpp`、`dotnet_host_loader.cpp` |
| CMake host pack | `CMakeLists.txt`（`DOTNET_HOST_PACK`） |
| Stage 必选文件 | `scripts/stage.ps1`（`nethost.dll`、`runtimeconfig.json`） |
| Launch 检查 | `scripts/launch.ps1` |
| 加载契约文档 | `docs/creo-host-loading.md` |
| Agent ACL 管道 | `src/CreoToolkit.Agent/Transport/NamedPipeAgentServer.cs` |
| Agent JSONL 客户端 | `src/CreoToolkit.Agent.Client/NamedPipeAgentClient.cs` |
| WPF-UI | `samples/CreoToolkit.Samples.Shell.Wpf/CreoToolkit.Samples.Shell.Wpf.csproj` |
| `ICreoNative` DIM | `src/CreoToolkit.Sdk/Native/ICreoNative.cs` |
| FrozenDictionary | `src/CreoToolkit.Sdk/Errors/ProErrorPolicy.cs` |
| `required` 属性 | `src/CreoToolkit.App/CommandMetadata.cs` |
| Index/Range | `samples/CreoToolkit.Samples.Shell.WinForms/ShellCommandBrowser.cs` |
| `ReadAsync(Memory)` | `src/CreoToolkit.Agent/Transport/NamedPipeAgentServer.cs` |
| `CTK_HOST_RUNTIME_CONFIG` | `src/CreoToolkit.App/CtkEnv.cs`、`host_entry.cpp`、`scripts/launch.ps1` |

---

## 14. 统计快照（本分析扫描）

| 项 | 值 |
|---|---|
| csproj | 35（34 个纯 net8 / net8-windows，1 个双目标 → 全部收成 net472） |
| 手写 `.cs` | 291 |
| Generated `.cs` | 496 |
| file-scoped namespace 文件 | 783 |
| `readonly record struct` 文件 | 32 |
| `ICreoNative` DIM | **约 50** 个默认实现 |
| `ArgumentNullException.ThrowIf*` 文件 | 92 |
| `ArgumentException.ThrowIf*` 文件 | 51 |
| `UnmanagedCallersOnly` | 4 个方法，全部在 Host |
| `AssemblyLoadContext` / `NativeLibrary` / `AssemblyDependencyResolver` | 仅 Host |
| `FrozenDictionary` | 1 文件 |
| `required` 属性 | 2（`CommandMetadata`） |
| Index/Range | 1（WinForms Shell） |
| `LibraryImport` | 0 |
| `Microsoft.Extensions.*` | 0 |
| 已有 `#if NET472` / `#if NET6_0_OR_GREATER` | 仅 Interop 2 处业务 + 1 处 polyfill |
| PackageReference 种类 | Serilog 三件套、System.Memory、DiagnosticSource、STJ、WPF-UI |

以上数字用于估工作量；树外测试未计入。

---

## 15. 第二轮遗漏补查（相对第一稿）

按「整仓改为 net472」把第一稿漏掉或写轻了的点列在这里。**加粗**是编译/启动级。

### 15.1 第一稿漏掉的编译阻断

| 项 | 位置 | 为何漏 | 处理 |
|---|---|---|---|
| **`ICreoNative` DIM × ~50** | `src/CreoToolkit.Sdk/Native/ICreoNative.cs` | 第一稿写「DIM 0 处」，只扫了 Interop 的桥接口；Sdk 后加能力用默认实现喂 fake | 拆扩展接口或抽象基类，见 §4.5 |
| **`FrozenDictionary`** | `ProErrorPolicy.cs` | 只扫了 ThrowIf / ALC | 改 `Dictionary` |
| **`Stream.ReadAsync(Memory<byte>, ct)`** | Agent `BoundedLineReader` | 只写了 ReadLineAsync(ct) | 改 `byte[]` 重载 |
| **`Math.Clamp`** | `PtGeomRegistration.cs` | sample 边角 | 手写 clamp |
| **`ArgumentOutOfRangeException.ThrowIfNegative*`** | Sdk 3 个文件 | 被 ThrowIfNull 统计盖住 | 并入 ThrowHelper |
| **`string.StartsWith(char)`** | `DiagnosticBundle.cs` | 未单独搜 char 重载 | 改字符串重载 |
| **`[^LogMaxLines..]`** | `ShellCommandBrowser.cs` | 第一稿认为仓库没用 Index/Range | 改 Skip/Take |
| **`required` 成员** | `CommandMetadata.cs` | 被 Agent 参数名 `required:` 噪音淹没 | 仓级 `RequiredMember` polyfill |
| **`IsExternalInit` 作用域** | Interop `_Polyfills` 为 `internal` | 第一稿写「提升 polyfill」但没写清 **别的项目看不见** | 独立共享 polyfill 项目或 `Directory.Build.props` 编译链接 |

### 15.2 第一稿写过、整仓替换时要收口的

| 项 | 收口 |
|---|---|
| Interop `net8.0;net472` | 删 net8，条件包变无条件 |
| Host `EnableDynamicLoading` / RID / runtimeconfig | 删除；Stage 不再校验 nethost / runtimeconfig |
| `CTK_HOST_RUNTIME_CONFIG` | CtkEnv、host_entry.cpp、launch.ps1 三处一起删 |
| `LPUTF8Str` | Host `NativeBootstrapHandoff` 改 LPStr/UTF-16 |
| WinForms `net8.0-windows` | `net472` + `UseWindowsForms`（不要 `net472-windows`） |
| 文档路径 `bin\Debug\net8.0-windows\` | 改 `net472` |
| `deploy/` 里现成的 runtimeconfig / nethost | 视为过期产物，Stage 重建 |

### 15.3 仍建议保持、不要当成漏项去改的

| 项 | 原因 |
|---|---|
| file-scoped namespace、record、switch 表达式、using 声明、`nint`、`with` | `LangVersion=latest` 在 net472 上可编 |
| Generated 496 个 `DllImport` | 生成器已按 Framework 可编译形态输出 |
| NativeAbi C ABI / `exports.def` | 与 CLR 无关 |
| `ICommandBridge` / `IMenuBridge` 拆分、`RadioGroupItem` 不用 record | 已是 net472 友好设计，保留 |
| SDK 风格 csproj / `.slnx` | 用现代 SDK 编 net472；不必退回旧 csproj / packages.config |
| `ActivitySource` | 在 Interop，net472 已引 DiagnosticSource 8.0.1 |
| `JsonIgnoreCondition` / `JavaScriptEncoder` | 随 STJ NuGet，不是 inbox 依赖 |
| `CancellationTokenSource.CreateLinkedTokenSource` | net472 BCL 已有 |
| `AppDomain.CurrentDomain.GetAssemblies`（DiagnosticBundle） | Framework 上更自然 |
| `ZipFile` | net472 的 `System.IO.Compression.FileSystem` |
| WPF-UI 4.0.2 | 包声明支持 net462；只需真机确认 Mica |

### 15.4 本树当时未收录、但整仓替换会碰到

| 项 | 证据 | 影响 |
|---|---|---|
| `tests/` 下 xunit 工程 | InternalsVisibleTo 点名 Host/App/Sdk/Interop/Agent/WinForms.Tests；CMakeLists 有 `EXISTS` 守卫 | 全部要改 TFM；ALC / UnmanagedCallersOnly / DIM fake 用例会红 |
| `tools/CreoToolkit.Generator` | Interop `.targets` 指向它；本仓未收录生成器源码 | 生成器本身是 Python，TFM 无关；不要为了 Framework 去改生成语法 |
| `tools/check-*.ps1` | 当时由 `Directory.Build.targets` 调用 | 与 TFM 无关 |
| CI YAML | 本树无 `.github/` | CI 里的 `DOTNET_HOST_PACK`、Desktop Runtime 8、`-f net8.0` 都要改 |
| `deploy/` 已提交的 net8 产物 | `CreoToolkit.Host.runtimeconfig.json`、`nethost.dll` | 不要当运行时真源；Stage 重做 |

### 15.5 部署/运维漏项

| 项 | 说明 |
|---|---|
| 无 `app.config` | 必须 Host 内 `AssemblyResolve`，不要指望 `CreoToolkit.Host.dll.config` |
| `xtop.exe.config` | 不要让客户改 Creo 安装目录；文档写明禁止这条路 |
| Framework 引用程序集 | CI 装 4.7.2 Developer Pack 或 `Microsoft.NETFramework.ReferenceAssemblies.net472` |
| 客户机运行时 | 从「.NET 8 Desktop Runtime x64」改为「.NET Framework 4.7.2/4.8」（Win7 需离线安装包） |
| `DOTNET_ROOT` / `DOTNET_HOST_PACK` | CMake、launch、NativeHost README 全部删除 |
| CONTRIBUTING.md | 仍写 `dotnet build CreoToolkitRefactor.slnx`，可保留（SDK 编 net472）；但「.NET SDK 版本」表述要改 |
| `docs/event-catalog.md` / `docs/diagrams/class-diagram.md` | 若提到 CoreCLR / ALC / net8 路径，一并改 |

### 15.6 本轮仍未核实（边界）

下列在当时树内 **搜不到使用**，实施时若测试工程出现再处理，不阻塞主清单：

- `LibraryImport`、`delegate* unmanaged`、`IAsyncEnumerable`、`ModuleInitializer`、`SkipLocalsInit`
- `Microsoft.Extensions.*`、Generic Host、MEL `ILogger`
- `Channel<T>`、`PipeReader`、`TimeProvider`、`PeriodicTimer`、`DateOnly`/`TimeOnly`
- `SHA256.HashData`、`Random.Shared`、`Path.Join`、`Path.GetRelativePath`、`Enum.GetValues<T>`
- `CollectionsMarshal`、`NativeMemory`、`UnsafeAccessor`、JSON source generator
- WebView2、`Application.SetHighDpiMode`

树外测试工程不在本快照，**不能声称测试侧零遗漏**。

---
