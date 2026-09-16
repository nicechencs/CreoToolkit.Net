# CreoToolkit

[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.7.2-512BD4)](https://dotnet.microsoft.com/)
[![Creo](https://img.shields.io/badge/Creo-4.0%20M140-orange)](https://www.ptc.com/en/products/creo)
[![Buy Me A Coffee](https://img.shields.io/badge/-Buy%20me%20a%20coffee-FFDD00?logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/chencs)

**Creo Toolkit → C#/.NET 的对象化封装**。让 PTC Creo 的二次开发离开 C/VB 时代,用 .NET Framework 4.7.2 写,头文件为唯一真源,跨 Creo 版本可重生。

业务可纯 .NET;native dll 只剩一颗;托管侧 5900 个 Pro Toolkit 函数全直绑,无 wrapper 中间层。

运行方式是 **C/C++ Pro/Toolkit DLL（`startup dll`）+ hosted .NET Framework 4.x**：Creo 直接加载
`CreoToolkit.NativeHost.dll`，再由它通过 mscoree 启动 CLR 4。项目不是 J-Link 插件，也不需要部署 Java/J-Link runtime。

> **CreoToolkit is an independent open-source project. It is NOT affiliated with, endorsed by, or sponsored by PTC Inc.** See [`TRADEMARKS.md`](TRADEMARKS.md).

> 想看跑起来效果 → 跳 [§ 我想…… 跑 sample](#我想-跑-sample)

---

## 前置依赖

| 软件 | 版本 | 用途 |
|---|---|---|
| .NET SDK（`dotnet` CLI） | 能编 SDK-style `net472` 即可（本仓用 8/9/10 验证过） | **只用于开发机构建**，不装进 Creo、也不替代 Framework |
| .NET Framework | 4.7.2+ (x64) | **Creo 进程内唯一运行时**（CLR `v4.0.30319`）+ WinForms/WPF |
| Visual C++ 2015 Update 3 | VC140 x64 tools | Creo 4 production native ABI build（默认） |
| CMake | 3.25+ | native configure；可使用 VS 2022 随附 CMake |
| Visual Studio 2022 | Community+ | managed development + optional v143 compatibility build |
| Creo Parametric | 4 M140（主验证） | 已完成 native ABI、托管绑定与真机端到端验证 |
| Creo 9 M060 | 绑定生成可用，真机兼容性待确认 | 需同时指定对应 Toolkit SDK、重新生成托管绑定并完成 native ABI 与真机端到端验证 |
| Python 3 + `libclang` | 可选 | 默认 Creo4 使用已提交绑定；切换/重生其他 Creo 版本绑定时需要（`pip install libclang`） |

---

## 我想…… 跑 sample

让 Creo 弹起来。WinForms sample 会注册 24 个模块：`CreoToolkit` 顶级菜单提供 7 个主题对话框入口，
对话框内可运行 109 条模块命令；连同 7 个 dialog launcher，命令元数据共 116 条。
`ExtCreoMenu` 另演示子菜单、radio/check option command。Core edition 不提供 7 个主题对话框，主要用于 nogui/集成场景。

```cmd
git clone <repo-url>
cd <repo-dir>

REM build native + managed，构建 Stage，校验后发布到 deploy/
scripts\publish-deploy.ps1 -Clean
deploy\start-protk-samples-winforms.bat
```

> Creo 路径不对?设环境变量 `CTK_CREO_LAUNCHER=<你的 parametric.bat>` 再跑。

---

## 我想…… 改一行 sample 代码看效果

```cmd
REM 改 samples\Modules\CreoToolkit.Samples.PtBasic\PtBasicRegistration.cs
dotnet build CreoToolkitRefactor.slnx -c Release
scripts\stage.ps1 -Configuration Release -BuildNative
scripts\deploy.ps1 -StageRoot artifacts\stage\Release -Destination deploy
deploy\start-protk-samples-winforms.bat
```

> `dotnet build` 只更新标准 `bin/obj`，不会写 `deploy/`。开发时可直接运行上面的 Stage/Deploy；
> `scripts\publish-deploy.ps1` 是兼容快捷入口，内部也是 Stage → Deploy。

---

## 我想…… 写一个新 Module

3 步:

1. **复制一个最简 Module 当模板**(推荐 `samples\Modules\CreoToolkit.Samples.PtBasic`)
   - csproj 改名
   - `<NamespaceName>` / `<RootNamespace>` 改名
   - `PtBasicRegistration.cs` 改名 + 改命令名(必带项目前缀 `mymodule.<verb>`)

2. **在唯一目录真源中登记 Module**:
   - 打开 [samples/SampleCatalog.props](samples/SampleCatalog.props)，新增一条 `CtkSampleModule`。
   - 填写稳定的 `Include`、`Order`、`ProjectRelativePath`、`RegistrationType`、`RegistrationArguments`。
   - 不要手改聚合宿主的 `ProjectReference` 或注册调用；MSBuild 会生成 `SampleModuleCatalog.g.cs` 并把项目引用带入 Core/WinForms。

3. **加命令元数据**(单一真源,txt 是生成物不手编):
   - 在 [samples/CreoToolkit.Samples.ProtkAppls.Core/SampleCommandMetadata.cs](samples/CreoToolkit.Samples.ProtkAppls.Core/SampleCommandMetadata.cs) 的 `All` 数组加一条(Name/LabelKey/Label/Help/Kind/Danger/Dialog/Tab 一处声明)
   - 用 `MsgFileGenerator.Generate(SampleCommandMetadata.All)` 输出更新 `protk_samples.txt`
   - Shell 对话框分组由元数据 Dialog/Tab 字段自动驱动,无需再改 Catalog

`dotnet build` → Stage → Deploy → 启 Creo。build 阶段的 lint（命令名前缀 / msg key / 架构边界）会拦下常见错误。

---

## 我遇到了报错

| 报错关键字 | 一行修复 | 详细 |
|---|---|---|
| Creo launcher 找不到 | `set CTK_CREO_LAUNCHER=<完整路径>` | deploy launcher 不硬编码 Creo 安装路径，必须由参数或环境变量注入 |
| sample dll 缺 | `scripts\publish-deploy.ps1 -Clean` | build/Stage 不完整，或 Module 未登记到 `SampleCatalog.props` |
| `protk.host.dat` 缺 | `scripts\publish-deploy.ps1 -Clean` | NativeHost/Stage 未完成；不要手工拼 deploy 目录 |
| managed/Sdk.dll 缺/旧 | `scripts\publish-deploy.ps1 -Clean` | 普通 build 不生成运行时目录；Stage 负责 publish Host/Sdk/Interop/App |
| `TypeLoadException: CreoMat4/Plane/CoordSystem` | 重建并重新 Stage/Deploy | app 与 `managed/` 中的共享 SDK 版本漂移 |
| `FileNotFoundException: CreoToolkit.Samples.*` | 重建并重新 Stage/Deploy | Module 未登记到 Catalog，或 app 依赖未进入 Stage |
| `PRO_TK_MSG_NOT_FOUND` / `PRO_TK_GENERAL_ERROR` | 检查 sample msg 文件 + `protk.dat` 的 `text_dir` | msg key 与 txt 文件不对齐 |
| CLR 4 未启动 | 安装/启用 .NET Framework 4.7.2+ x64 | NativeHost 经 mscoree 加载 `v4.0.30319` |
| `DllNotFoundException: CreoToolkit.NativeHost` | 确认 Creo 已 LoadLibrary NativeHost.dll | P/Invoke 绑到进程内已加载的同一 DLL |

---

## 构建

```cmd
REM 全 sln（只写 bin/obj，不写 deploy/）
build-dotnet.cmd                                    REM = dotnet restore + build -c Release
build-dotnet.cmd Debug                              REM 切 Debug
dotnet build CreoToolkitRefactor.slnx -c Release    REM 等价裸命令

REM 清 .NET 产物(dotnet clean + 递归 prune bin/obj)
scripts\clean-dotnet.cmd

REM Native(C++,必须 VS Developer Command Prompt 或装好 cl.exe)
build_native.cmd CreoToolkit.NativeHost Release    REM 运行时 native dll

REM 运行包（可拆开执行，便于 CI/发布审计）
scripts\stage.ps1 -Configuration Release -BuildNative
scripts\deploy.ps1 -StageRoot artifacts\stage\Release -Destination deploy
```

**绑定预生成入仓**：默认 `Creo4_M140` 的 `src/CreoToolkit.Interop/Generated/Creo4_M140/*.g.cs` 已跟踪，
使用默认版本时无需 Python/libclang。切换到其他 Creo 版本需传 `-p:CreoVersion=<版本>`；若该版本没有已提交产物，
必须同时具备生成器、Python+libclang 和对应 Toolkit 头文件。生成成功只代表托管绑定可编译，不等于 native ABI 或真机运行已验证。

---

## 深入文档

- **架构总览（分层 L0-L4 + 进程内加载链）**: [docs/architecture-overview.md](docs/architecture-overview.md)
- **对象模型类图**（五张图,分层→对象→句柄→命令路径→直绑流向）: [docs/diagrams/class-diagram.md](docs/diagrams/class-diagram.md)
- **宿主加载流程图**（drawio 六页：总览 → Native → Bootstrap → LoadFrom → Run → 停止）: [docs/diagrams/host-loading.md](docs/diagrams/host-loading.md) · [host-loading.drawio](docs/diagrams/host-loading.drawio)
- **宿主加载、配置与卸载契约**: [docs/creo-host-loading.md](docs/creo-host-loading.md)
- **event 命名速查表**（结构化日志 event 键清单与命名规则）: [docs/event-catalog.md](docs/event-catalog.md)
- **SDK 对象模型、事件与异常契约**（六原则 / R1-R5 命名规则 / 验收标准）: [src/CreoToolkit.Sdk/README.md](src/CreoToolkit.Sdk/README.md)
- **NativeAbi 源码组织**（L1 C ABI 共享源码池，非独立 DLL）: [src/CreoToolkit.NativeAbi/README.md](src/CreoToolkit.NativeAbi/README.md)
- **NativeHost 构建与客户 runtime 部署**（VC140 生产工具链 + framework-dependent 默认通道）: [src/CreoToolkit.NativeHost/README.md](src/CreoToolkit.NativeHost/README.md)
- **构建 / Stage / Deploy 脚本**（stage.ps1 / deploy.ps1 / publish-deploy.ps1）: [scripts/README.md](scripts/README.md)
- **内存所有权编目**（P/Invoke 三态治理与 `*Free` 编目）: [interop-manifest/README.md](interop-manifest/README.md)

---

## 许可与商业模式

- **代码开源**:本仓库代码采用 [Apache License 2.0](LICENSE),个人 / 商业项目均可免费使用、修改、分发,**附带专利反诉触发条款**(若使用者对作者发起专利诉讼,自动失去许可)。
- **分发义务**:再分发(源码或二进制)须随附 [LICENSE](LICENSE) + [NOTICE](NOTICE);若修改了源文件,须在该文件加显著标记说明改动。
- 上游 Creo Toolkit / J-Link / VB API 等 PTC 资产的许可与商业条款,以 PTC 官方协议为准,本仓库不重新授权。

---

## 付费咨询、定制开发

联系邮箱 nicechencs@gmail.com

---

## 支持项目

如果这个项目对你有帮助,可以 [☕ 请我喝杯咖啡](https://buymeacoffee.com/chencs)。

---

## 贡献

欢迎 PR、Issue 与讨论。请先读 [`CONTRIBUTING.md`](CONTRIBUTING.md) 与 [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md)。

## Security

发现安全问题请按 [`SECURITY.md`](SECURITY.md) 流程私下报告。
