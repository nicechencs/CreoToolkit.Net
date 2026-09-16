# CreoToolkit 架构总览

**一句话定位**：把 PTC Creo Parametric 的 Pro/Toolkit C API 封装成地道的 .NET Framework 4.7.2 对象
模型，让二次开发离开 C/VB 时代，业务代码可纯 .NET，头文件为唯一真源，跨 Creo 版本可
重生。

运行形态是一颗 native DLL + hosted .NET：Creo 通过 `protk.dat` 加载
`CreoToolkit.NativeHost.dll`，NativeHost 再经 mscoree 启动 CLR 4 与托管入口。**运行时真正触达
Creo 的 native DLL 只有一颗（`CreoToolkit.NativeHost.dll`）**；托管 P/Invoke 库名为
`CreoToolkit.NativeHost`（进程内已加载的同一 DLL）。

## 分层结构

```mermaid
flowchart TB
    Creo["Creo Parametric<br/>(Pro/Toolkit host, 主线程)"]
    L0["L0 · 生成管线<br/>(Creo Toolkit 头文件 → gen_bindings.py → *.g.cs)"]
    L1["L1 · NativeAbi (C/C++)<br/>Ctk_* C ABI · 内存三态释放门 · 编入 NativeHost.dll"]
    L2["L2 · Interop (.NET)<br/>手写 Ctk_* + 生成 Pro* P/Invoke · SafeHandle · CtkAbi.Verify"]
    L3["L3 · Sdk (.NET)<br/>对象模型 · CreoSession/Models/Selections/Features/... · 六原则 · 主线程门"]
    L4["L4 · App / Agent / Host (.NET)<br/>命令与菜单框架 · 声明式 builder · Bootstrap 托管入口 · Agent 门面"]

    L0 -. 头文件真源 .-> L1
    L0 -. Pro* g.cs .-> L2
    Creo -->|LoadLibrary + user_initialize| L1
    L1 -.mscoree + CLR 4.-> L4
    L4 --> L3
    L3 --> L2
    L2 -.P/Invoke.-> L1
```

- **L0 生成管线**：以 Creo Toolkit 头文件为唯一真源，`gen_bindings.py` 用 libclang
  抽 IR 后产出 `src/CreoToolkit.Interop/Generated/<Creo 版本>/**/*.g.cs`。切换 Creo 版
  本时重新生成即可；主分支默认 Creo 4 M140 的产物入仓。
- **L1 · NativeAbi**（`src/CreoToolkit.NativeAbi/`，C/C++）：稳定的 `Ctk_*` C ABI（命
  令桥、内存三态释放门、日志、回调）+ `exports.def` 声明的全量 Pro* 直导出。**不产
  出独立 DLL**，源码直接编入 `CreoToolkit.NativeHost.dll`。内存所有权契约按 data /
  live / borrowed 三态治理，配套 `interop-manifest/ownership.yml`。
- **L2 · Interop**（`src/CreoToolkit.Interop/`，.NET）：托管侧 P/Invoke 接缝——手写
  的 `Ctk_*` 与生成的 `Pro*` 两族方法、镜像结构体、`CtkAbi.Verify` 逐项校核尺寸/
  offset、`CreoSafeHandle` 体系。P/Invoke 库名是 `CreoToolkit.NativeHost`（进程内已
  加载的同一 DLL；无 sidecar、无 `DllImportResolver`）。
- **L3 · Sdk**（`src/CreoToolkit.Sdk/`，.NET）：对外的对象模型——`CreoSession` 为根，
  下挂 `Models` / `Selections` / `Features` / `Windows` / `Layers` / `Parameters` 等门
  面；类型骨架和六大原则（读写分离、会话绑定视图句柄、工厂构造、异常层级映射错误码、
  部分覆盖+显式降级、类型让操作类型安全）见
  [src/CreoToolkit.Sdk/README.md](../src/CreoToolkit.Sdk/README.md)。
- **L4 · App / Agent / Host**：
  - `CreoToolkit.App`：`ICreoApplication` 契约、声明式 builder、命令与菜单元数据；业
    务应用实现此接口即可被 Host 装载。
  - `CreoToolkit.Host`：`public static int Method(string)` 托管入口（`Initialize` /
    `InitializeApp` 等）、默认 AppDomain `LoadFrom` + `AssemblyResolve`、启动/终止编排。
  - `CreoToolkit.Diagnostics`：可选启动诊断（模型预载、命令、Ribbon）；`CTK_DIAGNOSTICS_ENABLED=0`
    完全绕过。
  - `CreoToolkit.Agent` / `CreoToolkit.Agent.Client`：Agent 路由、审计策略、命名管道
    服务，供外部进程按 verb 请求受控操作。

## 进程内加载链

```text
Creo → protk.dat → LoadLibrary(CreoToolkit.NativeHost.dll)
      → DllMain (bootstrapId + 单条 attach 证据)
      → user_initialize [Creo 主线程]
           → mscoree CLRCreateInstance → ICLRRuntimeHost::Start (v4.0.30319)
           → ExecuteInDefaultAppDomain(CreoToolkit.Host.Bootstrap.InitializeApp)
                → AssemblyResolve (Host/Sdk/Interop/Serilog 共享)
                → CreoSession.Attach → CtkAbi.Verify
                → [可选] Diagnostics.PrepareSession
                → LoadFrom 外部 ICreoApplication（默认 AppDomain）
                → CreoAppHost.Run（命令/菜单/Ribbon 注册 + OnInitialize）
```

托管 P/Invoke 回程：`NativeMethods.Ctk_*` → `DllImport("CreoToolkit.NativeHost")` →
已由 Creo 加载的 `CreoToolkit.NativeHost.dll` 内 `Ctk_* / Pro*`。

流程图（drawio 六页，含 Native 返回码、Bootstrap 三入口、LoadFrom、Run、停止）：
[diagrams/host-loading.md](./diagrams/host-loading.md) ·
[diagrams/host-loading.drawio](./diagrams/host-loading.drawio)。

详细契约（导出符号、环境变量、日志/诊断、命令注册顺序、停止策略）见
[creo-host-loading.md](./creo-host-loading.md)。

## 深入阅读

- [对象模型类图（五张图，分层→对象→句柄→命令路径→直绑流向）](./diagrams/class-diagram.md)
- [宿主加载流程图（drawio + mermaid 对照）](./diagrams/host-loading.md)
- [宿主加载、配置与卸载契约](./creo-host-loading.md)
- [event 命名速查表](./event-catalog.md)
- [SDK 对象模型、六原则、命名规则与验收尺](../src/CreoToolkit.Sdk/README.md)
- [NativeHost 构建、runtime 部署、客户安装通道](../src/CreoToolkit.NativeHost/README.md)
- [NativeAbi 源码组织与共享池模型](../src/CreoToolkit.NativeAbi/README.md)
- [内存所有权编目（三态治理与 `*Free` 编目）](../interop-manifest/README.md)
