# CreoToolkit.NativeAbi — L1 native ABI（共享源码，**非独立工程/DLL**）

本目录是 **L1 native ABI 的生产源码层**：稳定 C ABI 头文件（`include/ctk_*.h`）+ 实现
（`src/ctk_*.cpp`：命令桥 `ctk_app`、数据 ops `ctk_ops`、缓冲/句柄/日志/回调等）。
**测试/探针源（脱 Creo 单测、Creo 链接 smoke 探针）已分离到独立测试目录，不混入本生产目录。**

## 它不产出单一 DLL，而是被多个载体按需编入

这些 .cpp 的不同消费者**编译定义 / Creo 依赖各不相同**（如脱 Creo 测试要 `CTK_APP_TEST_NO_CREO`、
NativeHost 要 `PRO_USE_VAR_ARGS`+`CTK_OPS_NO_ENTRY_STUBS`），无法编成一个共享库，故为**共享源码池**，
由 `CMakeLists.txt` 直接编进各 target：

| 载体（CMake target） | 用途 |
|---|---|
| `CreoToolkit.NativeHost` | 生产载体：CLR 4（mscoree）宿主 + L1 ops + 命令桥 |
| `CreoToolkit.NativeAbi.OpsProbe` / `.CoreTest` / `.CreoLinkProbe` | 诊断探针 |
| `CreoGate.Sync` / `CreoGate.Async` | Creo 集成测试载体 |
| `ctk_app_test` / `ctk_mem_test` | 脱 Creo 单测 |

> 上表中 `NativeAbi.OpsProbe/.CoreTest/.CreoLinkProbe` 探针与 `ctk_app_test/ctk_mem_test` 单测均为
> **测试/探针**：其 CMake target 定义位于独立测试目录（根 CMake `add_subdirectory` 接入），
> 产物统一输出 `cmake/bin`，**绝不回灌本 src 生产目录**。
> `CreoGate.*` 为另一套集成测试载体。

## 唯一构建事实源 = 仓库根 `CMakeLists.txt`

- 命令行构建：`build_native.cmd <target> [Debug|Release]`（CMake wrapper；旧 `build_*.cmd` 亦同）。
- src 下各 `*.vcxproj` 仅是 **NMake wrapper**（调 `build_native.cmd`），**刻意不维护 `<ClCompile>` 源清单**
  ——源文件列表只在 `CMakeLists.txt`，杜绝手维护漂移（历史上 NativeHost.vcxproj 曾漏 `ctk_app.cpp`）。
- CMake 构建目录：`cmake/`（gitignore）。在 VS 里浏览/IntelliSense/调试 native：用其中生成的
  `cmake/CreoToolkitNative.sln` 或 VS「Open Folder」（CMake 集成）。顶层解决方案主要承载 .NET 工程。
