# CreoToolkit.NativeHost

Synchronous CLR 4 (mscoree) carrier and registered L1 C ABI bridge.

Creo loads `out\CreoToolkit.NativeHost.dll` through `protk.dat`; `user_initialize`
then hosts desktop CLR `v4.0.30319` through `ICLRRuntimeHost::ExecuteInDefaultAppDomain`
and calls `CreoToolkit.Host.Bootstrap`.
The same registered DLL also exports the `Ctk_*` L1 ABI used by managed
P/Invoke during hosted runs.

加载流程图（drawio）：[docs/diagrams/host-loading.md](../../docs/diagrams/host-loading.md)。

## Current Status

`InitializeApp` 主入口已在真实 Creo 4 M140 连续验证：CLR 4 hosting、native ABI 解析、`CtkAbi.Verify`、`CreoSession.Attach`、Model retrieve、Parameter round-trip、Selection、Feature.ElementTree.Walk、owned-resource balance 全部 PASS；手动菜单点击实证已通过。`Initialize` / `InitializeAndAttach` 仍可调用但非主路径，新代码请走 `InitializeApp` + `CTK_APP_ASSEMBLY` 模式。

> ### ⚠️ Breaking change
>
> `CTK_HOST_METHOD=InitializeAndSmoke` **no longer exists**. The entry point was removed and the smoke
> logic was migrated out of `CreoToolkit.Host` into a sample project. To run the model smoke from now on:
>
> ```bat
> set CTK_HOST_METHOD=InitializeApp
> set CTK_APP_ASSEMBLY=...\CreoToolkit.Samples.HostProbe.dll      :: or the aggregating CreoToolkit.Samples.ProtkAppls.WinForms.dll
> set CTK_APP_TYPE=CreoToolkit.Samples.HostProbe.HostProbeApp     :: or CreoToolkit.Samples.ProtkAppls.ProtkSimpleSamplesApp
> set CTK_APP_RUN_COMMAND=ctk.probe.smoke.model
> set CTK_HOST_SMOKE_MODEL=exercise4
> set CTK_HOST_ALLOW_MODEL_WRITE=1                                :: optional, enables parameter Set+Find round-trip
> ```
>
> Older `CTK_HOST_ENABLE_MODEL_OPS=1` / `CTK_HOST_ENABLE_EVENTS_SMOKE=1` env vars are retired entirely.
> The native-side model preload (`preload_model_if_requested` + `CTK_HOST_PRELOAD_MODEL` /
> `CTK_HOST_PRELOADED_MODEL_HANDLE`) was removed in a recent revision; model loading is owned by the managed
> `Bootstrap.LoadModelFromPathIfRequested` (`CTK_APP_LOAD_MODEL_PATH`).
> The events smoke (`SubscribeModelSavePre`) was retired entirely; events 现走 `SubscribeGroupUngroupPre` 三类示例范式。
- `Ctk_*` 业务 ABI 全部由 `out\CreoToolkit.NativeHost.dll` 服务；无 nethost sidecar。
  CLR 4 由系统 mscoree 提供。
- Probe / test DLL 命名 `CreoToolkit.NativeAbi.CoreTest.dll` / `OpsProbe.dll` / `CreoLinkProbe.dll`，不复制到 `out\managed`。

## Build

```bat
build_native.cmd CreoToolkit.NativeHost Release
build_native.cmd native-all Release
build_native.cmd native-all Release v143
```

`v140` is the default and required production toolset for the Creo 4 native ABI.
The build entry locates Visual C++ 2015 Update 3 x64 tools and selects the oldest
complete installed Windows 10 SDK that is compatible with VC140. Override only
when necessary with `CTK_VC140_ROOT`, `CTK_VC140_WINDOWS_SDK_ROOT`, or
`CTK_VC140_WINDOWS_SDK_VERSION`. The explicit `v143` command is a compatibility
regression; it does not replace the production VC140 gate.

`native-all` builds NativeHost, NativeAbi probes, CreoGate, and runs every native
test. The no-Creo managed roundtrip is `mscoree -> ICLRRuntimeHost ->
Bootstrap.Initialize` (needs Framework 4.x on the machine, not a Desktop Runtime).

The same target is exposed in Visual Studio through
`CreoToolkit.NativeHost.vcxproj` (NMake build calls `build_native.cmd` 同入口)。

Outputs:

- `out\CreoToolkit.NativeHost.dll`

Native build does not produce managed output. `dotnet build` writes normal project
`bin/obj`; `scripts\stage.ps1` publishes Host/Sdk/App/Interop/Diagnostics into
`artifacts\stage\<Configuration>\managed\`, and `scripts\deploy.ps1` copies that
verified Stage into the selected deployment root.

Managed P/Invoke uses `DllImport("CreoToolkit.NativeHost")` against the already-loaded
module. NativeHost calls `SetDllDirectory` on its directory before the first managed
call (it does **not** prepend that directory onto process `PATH`).

## Customer runtime and package policy

The customer machine must have **x64 .NET Framework 4.7.2+** (CLR `v4.0.30319`).
NativeHost uses `CLRCreateInstance` / `ICLRRuntimeHost` (`mscoree`); it does not
ship `nethost.dll` or a `runtimeconfig.json`.

Production native binaries are built with **Visual C++ 2015 Update 3 (VC140)**
+ dynamic CRT to match the Creo 4 native ABI baseline. Modern MSVC toolsets
are compatibility checks only; they do not replace the VC140 release gate.

The distribution layout produced by `scripts\stage.ps1` / `scripts\deploy.ps1`:

```text
deploy/
  native/
    CreoToolkit.NativeHost.dll
    protk.host.dat
  managed/
    CreoToolkit.Host.dll
    CreoToolkit.App.dll
    CreoToolkit.Sdk.dll
    CreoToolkit.Interop.dll
    CreoToolkit.Polyfills.dll
  apps/
    <app>/...
```

A package missing NativeHost.dll or the managed Host assembly is rejected by the
launcher/deploy scripts.

Verification: compile `clr_host_loader.cpp` against NETFXSDK `metahost.h` and
link `mscoree.lib`. Final release qualification runs a real Creo startup/terminate
smoke and checks structured evidence (JSONL `entry_returned` / `session_end`,
`latest-session.pointer`).

## Creo Smoke Run

`protk.host.dat` is a path-free template. Use a staged/deployed `start*.bat` (which calls
`launcher\launch.ps1`) or materialize both `exec_file` and `text_dir` before writing it
into the Creo working directory as `protk.dat`, then start Creo from that directory.
Do not copy the template into a Creo installation directory, and do not expect
`dotnet build src\CreoToolkit.Host` to recreate the retired `out\managed` layout.

Default logs:

- NativeHost picks the host log directory by this decision tree:
  1. env `CTK_BOOTSTRAP_LOG_DIR` (new name; legacy alias `CTK_HOST_LOG_DIR` still honored as fallback)
  2. NativeHost.dll sibling `..\logs\host\` (typical deploy layout)
  3. fallback `%LOCALAPPDATA%\CreoToolkit\logs\host\`
- **Retired**: the legacy `host-marker-*.log` / `host-native-*.log` files and
  the `latest.json` manifest are no longer written. The OTel-shaped
  `native-bootstrap-{iso}-p{pid}-{traceId32}.jsonl` file is the single
  source of truth for native bootstrap evidence.
- Consumers locate the current session file through
  `<host_log_dir>\latest-session.pointer` (single line, the JSONL file
  name). JSONL is the native bootstrap evidence source of truth. ETW is
  off unless NativeHost is built with `-DCTK_ENABLE_ETW=ON`.
- Retention is controlled by `CTK_BOOTSTRAP_LOG_RETENTION_DAYS`
  (new name; legacy alias `CTK_HOST_LOG_RETENTION_DAYS` still *read* as
  fallback; default 14; `<=0` disables cleanup). NativeHost no longer
  rewrites `CTK_HOST_LOG_DIR`. Hitting a legacy alias emits a
  `ctk.native_host.config.env_deprecated` WARN event. Applied to
  `native-bootstrap-*.jsonl` / leftover `session-index.jsonl` /
  `*.pointer` and `%LOCALAPPDATA%\CreoToolkit\ctk-attach-p*.log`.
- Managed (.NET) host log is owned by Serilog. Path is selected by
  `CTK_HOST_MANAGED_LOG` (base path, daily-rolled by Serilog) and
  defaults to `<AppContext.BaseDirectory>/logs/host-managed.log` when
  unset. Launcher scripts point it at `<work>/out/` so it co-locates
  with the native bootstrap dir when `CTK_BOOTSTRAP_LOG_DIR` (or its
  legacy alias `CTK_HOST_LOG_DIR`) shares that prefix.

Optional environment overrides:

- `CTK_HOST_ASSEMBLY`: absolute path to `CreoToolkit.Host.dll`. When unset, NativeHost probes
  `<NativeHost.dll dir>\managed` (development output) and then
  `<NativeHost.dll dir>\..\managed` (packaged `deploy\native` + `deploy\managed` layout).
- `CTK_HOST_RUNTIME_CONFIG`: leftover CoreCLR env name. The CLR 4 host does not read it.
- `CTK_HOST_METHOD`: managed entry method, default `InitializeApp`; explicit values are limited to
  `Initialize`, `InitializeAndAttach`, and `InitializeApp`. Any other value fails initialization with rc `-13`.
- `CTK_BOOTSTRAP_LOG_DIR`: overrides the host log directory entirely (legacy alias `CTK_HOST_LOG_DIR`)
- `CTK_BOOTSTRAP_LOG_RETENTION_DAYS`: retention days for host logs (default 14; legacy alias `CTK_HOST_LOG_RETENTION_DAYS`)
- `CTK_BOOTSTRAP_TRACE_ID`: NativeHost-published JSONL trace id. Do not set manually.
  Managed Host no longer P/Invokes `ctk_native_log_handoff_ack`; the native export remains as an ABI stub.
- `CTK_HOST_MANAGED_LOG`: managed log path (Serilog convention)
- `CTK_HOST_ALLOW_MODEL_WRITE`: enables parameter writes in `ctk.probe.smoke.model` (HostProbe sample)
- `CTK_HOST_SMOKE_MODEL`: model name for model smoke; default `exercise4`
- `CTK_HOST_SMOKE_MODEL_TYPE`: `Part`, `Assembly`, or `Drawing`; default `Part`
- `CTK_DOTNET_LOG_FILE`: managed-side base path override for the
  Serilog file sink (auto-appends `.log` if no extension). Legacy
  alias `CTK_LOG_FILE` is still read by `CreoLog.ReadEnvWithFallback`
  but emits a `config.env.deprecated` WARN; new launchers use the
  `CTK_DOTNET_LOG_*` family.
- `CTK_MESSAGEBAR_MIRROR`: when `on/1/true/yes`, mirror `ctx.Messages.Info(text)` to managed log
  with `event=messagebar.mirror.ok` + `props.cmd=<command>`; default off

Use `CTK_HOST_METHOD=InitializeAndAttach` only after the minimal
`Initialize` smoke proves CLR 4 enters the Creo process.
