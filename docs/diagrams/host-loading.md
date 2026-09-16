# 宿主加载流程图

可编辑原图：[host-loading.drawio](./host-loading.drawio)（diagrams.net 多页）。

契约正文仍以 [creo-host-loading.md](../creo-host-loading.md) 与源码为准；本页是对照视图。布局脚本：[_gen_host_loading.py](./_gen_host_loading.py)（在 diagrams.net 里手工改过图之后不要再跑脚本覆盖）。

## 怎么打开

| 方式 | 做法 |
|---|---|
| 浏览器 | 打开 [diagrams.net](https://app.diagrams.net/)，File → Open from → Device，选本文件 |
| VS Code | 安装 [Draw.io Integration](https://marketplace.visualstudio.com/items?itemName=hediet.vscode-drawio)，直接打开 `.drawio` |
| GitHub | 本页下方 mermaid 可渲染；`.drawio` 本身不在网页里预览 |

六页：

1. **总览 · 进程内加载链** — launcher → Creo → NativeHost → CLR 4 → Host → App
2. **Native · user_initialize** — DllMain 约束、重入守卫、返回码
3. **Managed · Bootstrap 入口** — 三方法 + `InitializeApp` 生产路径
4. **应用装配 · LoadFrom** — NoOpApp / allowlist / AssemblyResolve / SampleCatalog
5. **CreoAppHost.Run · 命令注册** — 硬失败命令 vs 可降级菜单
6. **停止 · user_terminate** — 处置对象，不卸 CLR / 不卸 DLL

---

## 1. 总览 · 进程内加载链

```mermaid
flowchart TB
    subgraph Launcher["启动器 / Creo"]
        BAT["start-*.bat / launch.ps1"]
        DAT["物化 WorkDir/protk.dat<br/>exec_file → NativeHost.dll"]
        ENV["注入 CTK_HOST_METHOD=InitializeApp<br/>CTK_HOST_ASSEMBLY / CTK_APP_*"]
        CREO["Creo Parametric 读 protk.dat"]
        BAT --> DAT --> ENV --> CREO
    end

    subgraph Native["NativeHost.dll"]
        LL["LoadLibrary NativeHost.dll"]
        DllMain["DllMain：HINSTANCE + bootstrapId<br/>loader lock 下只写 attach 证据"]
        UI["user_initialize（Creo 主线程）<br/>touch + SetDllDirectory"]
        LL --> DllMain --> UI
    end

    subgraph Clr["CLR 4 默认 AppDomain"]
        START["CLRCreateInstance → Start v4.0.30319"]
        EXEC["ExecuteInDefaultAppDomain<br/>Bootstrap.InitializeApp"]
        RUN["RunEntry + AssemblyResolve"]
        ATT["CreoSession.Attach + CtkAbi.Verify"]
        DIAG["可选 Diagnostics.PrepareSession"]
        START --> EXEC --> RUN --> ATT --> DIAG
    end

    subgraph App["业务 App"]
        LOAD["LoadFrom ICreoApplication 或 NoOpApp"]
        HOST["CreoAppHost.Run"]
        OK["返回 0 · 命令就绪"]
        LOAD --> HOST --> OK
    end

    CREO --> LL
    UI --> START
    DIAG --> LOAD
    HOST -.P/Invoke Ctk_*/Pro*.-> Native
```

运行时真正触达 Creo 的 native DLL 只有 `CreoToolkit.NativeHost.dll`。不部署 `nethost` / `hostfxr` / `runtimeconfig.json`。

---

## 2. Native · user_initialize

```mermaid
flowchart TB
    LL["Creo LoadLibrary NativeHost.dll"] --> DllMain["DllMain PROCESS_ATTACH"]
    DllMain --> UI["user_initialize"]
    UI --> G{"g_user_init_state"}
    G -->|2 成功后重入| R0["返回 0"]
    G -->|1 失败后重入| R14["返回 -14"]
    G -->|0 首次| PATHS["session 路径 / retention / JSONL / 可选 ETW"]
    PATHS --> TOUCH["ctk_toolkit_touch<br/>ProToolkitApplTextPathGet"]
    TOUCH -->|非 0| FAIL["finish_user_initialize(rc)"]
    TOUCH -->|0| PUB["publish_native_host_path"]
    PUB --> METH{"CTK_HOST_METHOD"}
    METH -->|非法| R13["返回 -13"]
    METH -->|合法或缺省 InitializeApp| INV["invoke_managed"]
    INV --> RESOLVE{"Host.dll 路径"}
    RESOLVE -->|缺| R10["返回 -10"]
    RESOLVE -->|命中| CLR["CLRCreateInstance / Start / ExecuteInDefaultAppDomain"]
    CLR -->|executed=false| R12["返回 -12"]
    CLR -->|executed| RC{"托管 rc"}
    RC -->|0| OK["state=2，返回 0"]
    RC -->|非 0| FAIL
```

DllMain 禁止堆、线程、`LoadLibrary`、递归 mkdir。Creo 对 `user_initialize` 非零通常不再调 `user_terminate`。

---

## 3. Managed · Bootstrap 入口

```mermaid
flowchart TB
    RUN["RunEntry：AssemblyResolve + 绑日志"] --> M{"CTK_HOST_METHOD"}
    M -->|Initialize| I1["只挂钩 Resolve，body=0"]
    M -->|InitializeAndAttach| I2["Attach + CtkAbi.Verify，无 App"]
    M -->|缺省 / InitializeApp| I3["生产路径"]

    I3 --> ID{"_appHost 已存在?"}
    ID -->|是| REJ["返回 -1"]
    ID -->|否| ATT["EnsureSession：Attach + Verify"]
    ATT --> DA{"Diagnostics Requested?"}
    DA -->|是| PREP["PrepareSession"]
    DA -->|否| LOAD
    PREP --> LOAD["CreoApplicationLoader"]
    LOAD --> HOST["CreoAppHost.ForHost + Run"]
    HOST --> POST["可选 RunAppActions"]
    POST --> OK["返回 0"]
    HOST -->|抛| FAIL["managed-init-failure.txt<br/>DisposeManagedHost → -1"]
```

`Initialize` / `InitializeAndAttach` 没有 in-tree `start-*.bat`。`Terminate` 只由 `user_terminate` 调用，不在 `initialize_method` 白名单里。

---

## 4. 应用装配 · LoadFrom

```mermaid
flowchart TB
    ENV{"CTK_APP_ASSEMBLY 与 CTK_APP_TYPE"}
    ENV -->|都空| NOOP["new NoOpApp()"]
    ENV -->|缺一| ERR["InvalidOperation"]
    ENV -->|都设| PATH["相对 BaseDirectory 解析 + allowlist"]
    PATH -->|不在根目录 / 文件不存在| REJ["REJECT"]
    PATH -->|通过| LF["Assembly.LoadFrom"]
    LF --> T["GetType + ICreoApplication + 无参构造"]
    T --> APP["CreateInstance"]
```

`AssemblyResolve` 先复用已加载副本，再只探测 Host / Sdk / Interop / `CTK_HOST_ASSEMBLY` 所在目录。Sample 模块来自编译期 `samples/SampleCatalog.props`，不是运行时扫盘。

---

## 5. CreoAppHost.Run

```mermaid
flowchart TB
    R["CreoAppHost.Run"] --> INIT["app.Initialize(builder)"]
    INIT --> DEF["BuildDefinition"]
    DEF --> PIN["CommandDispatchPin + BridgeInitialize"]
    PIN --> CMD["CommandActionAdd + Designate（硬失败）"]
    CMD --> MENU["菜单 / option / Ribbon（可降级 WARN）"]
    MENU --> ON["OnInitialize → _lifecycleInitialized"]
    ON --> OK["host.init.ok"]
```

命令回调：`0` 成功 / `1` handler 异常 / `2` 未注册。F5（`OnInitialize` 抛）不调 `OnTerminate`。

---

## 6. 停止 · user_terminate

```mermaid
flowchart TB
    T["user_terminate"] --> G{"首次进入?"}
    G -->|否| NOOP["WARN no-op"]
    G -->|是| CLR{"CLR 已 Start?"}
    CLR -->|否| NAT
    CLR -->|是| TERM["Bootstrap.Terminate"]
    TERM --> D["DisposeManagedHost"]
    D --> AH["appHost.Dispose：OnTerminate + BridgeTerminate"]
    AH --> SD["session.Dispose"]
    SD --> NAT["不 Stop CLR · 不 FreeLibrary"]
    NAT --> DET["DLL_PROCESS_DETACH：loader lock 下无 IO"]
```

同进程不承诺再 `LoadFrom` 另一个 app。要换 app 就重启 Creo。
