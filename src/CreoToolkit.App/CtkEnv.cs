namespace CreoToolkit.App;

/// <summary>
/// CreoToolkit env 变量名集中常量。
/// 新增 env 变量必须在此声明,避免字符串字面散落 + typo + IDE 重命名困难。
/// <para>不含 sample/test/gate 专属变量(CTK_SAMPLE_*/CTK_GATE_*/CTK_REAL_CREO 等),
/// 那些保留在各自模块的 grep 即可定位。CreoApplicationLoader.AssemblyEnv/TypeEnv
/// 是 public const 向后兼容字段,值与 <see cref="AppAssembly"/>/<see cref="AppType"/>
/// 完全相同,保留不动。</para>
/// </summary>
public static class CtkEnv
{
    // ===== App 业务 =====
    /// <summary>外部 sample 程序集路径(CreoApplicationLoader.Create 入口)。</summary>
    public const string AppAssembly = "CTK_APP_ASSEMBLY";
    /// <summary>外部 sample ICreoApplication 类型全名(CreoApplicationLoader.Create 入口)。</summary>
    public const string AppType = "CTK_APP_TYPE";
    /// <summary>sample 加载路径白名单(CreoApplicationLoader 路径校验,';' 分隔)。</summary>
    public const string AppAllowedRoots = "CTK_APP_ALLOWED_ROOTS";
    /// <summary>msg 文件名(&lt;= 39 ASCII,HostStartupOptions)。</summary>
    public const string AppMsgFile = "CTK_APP_MSG_FILE";
    /// <summary>启动期 diagnostic 命令名(DiagnosticsStartupOptions + RunCommandForDiagnostics)。</summary>
    public const string AppRunCommand = "CTK_APP_RUN_COMMAND";
    /// <summary>启用 ribbon 加载 flag(1/true/yes/on,DiagnosticsStartupOptions)。</summary>
    public const string AppLoadRibbon = "CTK_APP_LOAD_RIBBON";
    /// <summary>ribbon 文件路径(DiagnosticsStartupOptions)。</summary>
    public const string AppRibbonFile = "CTK_APP_RIBBON_FILE";
    /// <summary>启动时打开会话内已加载模型名(StartupDiagnostics)。</summary>
    public const string AppPreloadModel = "CTK_APP_PRELOAD_MODEL";
    /// <summary>启动时从盘加载模型(.prt/.asm/.drw/...,StartupDiagnostics)。</summary>
    public const string AppLoadModelPath = "CTK_APP_LOAD_MODEL_PATH";
    /// <summary>Diagnostics 激活开关。0/off 强制关闭；1/on 显式启用；未设置时由旧诊断变量自动激活。</summary>
    public const string DiagnosticsEnabled = "CTK_DIAGNOSTICS_ENABLED";
    /// <summary>Diagnostics 必须成功开关；失败升级为 Host 启动失败。</summary>
    public const string DiagnosticsRequired = "CTK_DIAGNOSTICS_REQUIRED";
    /// <summary>sample 目标模型名(无扩展名),覆盖 app 默认 exercise4(ProtkSimpleSamplesApp 构造)。</summary>
    public const string AppModelName = "CTK_APP_MODEL_NAME";
    /// <summary>sample 目标模型类型(part/assembly/drawing/...),配合 <see cref="AppModelName"/>。</summary>
    public const string AppModelType = "CTK_APP_MODEL_TYPE";
    /// <summary>补充允许的 Creo 系统菜单名（';' 分隔）。</summary>
    public const string AppSystemMenuExtra = "CTK_APP_SYSMENU_EXTRA";

    // ===== App runtime 行为 =====
    /// <summary>命令完成/失败后向 Creo message bar 回显。</summary>
    public const string DispatchEcho = "CTK_DISPATCH_ECHO";
    /// <summary>把 message bar 业务输出镜像到 managed log。</summary>
    public const string MessageBarMirror = "CTK_MESSAGEBAR_MIRROR";

    // ===== Host 路径 =====
    /// <summary>managed JSONL log 文件路径(Bootstrap.SetFile 绑定)。</summary>
    public const string HostManagedLog = "CTK_HOST_MANAGED_LOG";
    /// <summary>native log 文件路径(DiagnosticBundle 采集)。</summary>
    public const string HostNativeLog = "CTK_HOST_NATIVE_LOG";
    /// <summary>marker log 文件路径(DiagnosticBundle 采集启动期 marker)。</summary>
    public const string HostMarkerLog = "CTK_HOST_MARKER_LOG";
    /// <summary>native bootstrap 日志目录显式覆盖(主名称;优先于 dll-相对/LOCALAPPDATA fallback)。</summary>
    public const string BootstrapLogDir = "CTK_BOOTSTRAP_LOG_DIR";
    /// <summary>native bootstrap 日志保留天数(主名称;默认 14;&lt;=0 disable cleanup)。</summary>
    public const string BootstrapLogRetentionDays = "CTK_BOOTSTRAP_LOG_RETENTION_DAYS";
    /// <summary>NativeHost 生成的本次 bootstrap W3C trace id（写入 JSONL / managed session.start）。</summary>
    public const string BootstrapTraceId = "CTK_BOOTSTRAP_TRACE_ID";
    /// <summary>deprecated:改用 <see cref="BootstrapLogDir"/>。</summary>
    [Obsolete("Use BootstrapLogDir (CTK_BOOTSTRAP_LOG_DIR); CTK_HOST_LOG_DIR 仅作兼容兜底。")]
    public const string HostLogDir = "CTK_HOST_LOG_DIR";
    /// <summary>deprecated:改用 <see cref="BootstrapLogRetentionDays"/>。</summary>
    [Obsolete("Use BootstrapLogRetentionDays (CTK_BOOTSTRAP_LOG_RETENTION_DAYS); CTK_HOST_LOG_RETENTION_DAYS 仅作兼容兜底。")]
    public const string HostLogRetentionDays = "CTK_HOST_LOG_RETENTION_DAYS";
    /// <summary>NativeHost.dll 路径(host_entry 启动前 SetDllDirectory；DiagnosticBundle 采集)。</summary>
    public const string HostNativeDll = "CTK_HOST_NATIVE_DLL";
    /// <summary>managed Host 程序集(host_entry.cpp,DiagnosticBundle 采集)。</summary>
    public const string HostAssembly = "CTK_HOST_ASSEMBLY";
    /// <summary>遗留 env：CoreCLR runtimeconfig。CLR 4 宿主不再读取；DiagnosticBundle 仍可采集。</summary>
    public const string HostRuntimeConfig = "CTK_HOST_RUNTIME_CONFIG";
    /// <summary>managed entry 方法(host_entry.cpp,DiagnosticBundle 采集)。</summary>
    public const string HostMethod = "CTK_HOST_METHOD";
    /// <summary>HostProbe smoke 测试模型名(samples HostProbeRegistration)。</summary>
    public const string HostSmokeModel = "CTK_HOST_SMOKE_MODEL";
    /// <summary>HostProbe smoke 测试模型类型(samples HostProbeRegistration)。</summary>
    public const string HostSmokeModelType = "CTK_HOST_SMOKE_MODEL_TYPE";
    /// <summary>deprecated/no-op:原 native 模型预载变量,保留一版仅为源码兼容。</summary>
    [Obsolete("Native model preload was removed; use AppLoadModelPath or AppPreloadModel.")]
    public const string HostPreloadModel = "CTK_HOST_PRELOAD_MODEL";
    /// <summary>deprecated/no-op:原 native 模型操作开关,保留一版仅为源码兼容。</summary>
    [Obsolete("Native model preload was removed; this environment variable is no longer read.")]
    public const string HostEnableModelOps = "CTK_HOST_ENABLE_MODEL_OPS";
    /// <summary>protk.dat 完整路径(DiagnosticBundle 显式 override)。</summary>
    public const string HostProtkDat = "CTK_HOST_PROTK_DAT";
    /// <summary>protk.dat 所在目录(DiagnosticBundle 备选探测)。</summary>
    public const string HostProtkDatDir = "CTK_HOST_PROTK_DAT_DIR";

    // ===== Log 全局(CreoLog,.NET 端) =====
    // 拆 native/dotnet env 之后,.NET 端 hot path 用 CTK_DOTNET_LOG_*。
    // 旧名 CTK_LOG / CTK_LOG_LEVEL / CTK_LOG_FORMAT / CTK_LOG_FILE / CTK_LOG_RETENTION_DAYS
    // 仍作 CreoLog.cs ReadEnvWithFallback 兜底,每个旧 env 触发一次 deprecated WARN。
    // CreoLog 直接硬编码字符串,这里的 const 仅供 DiagnosticBundle.All 白名单与外部消费者引用。
    /// <summary>.NET 日志总开关(on/off)。</summary>
    public const string DotnetLog = "CTK_DOTNET_LOG";
    /// <summary>.NET 日志级别(error/warn/info/trace)。</summary>
    public const string DotnetLogLevel = "CTK_DOTNET_LOG_LEVEL";
    /// <summary>.NET 日志格式(text|json|both)。</summary>
    public const string DotnetLogFormat = "CTK_DOTNET_LOG_FORMAT";
    /// <summary>.NET 日志文件 base path(覆盖默认)。</summary>
    public const string DotnetLogFile = "CTK_DOTNET_LOG_FILE";
    /// <summary>.NET 日志滚动保留天数(默认 14)。</summary>
    public const string DotnetLogRetentionDays = "CTK_DOTNET_LOG_RETENTION_DAYS";
    /// <summary>环境基线(development=trace/info+text+json / production=warn+json)。</summary>
    public const string Env = "CTK_ENV";

    // 旧名常量,deprecated。CreoLog.cs 仍提供 ReadEnvWithFallback 兜底,DiagnosticBundle
    // 把新旧两个都纳入 manifest 便于排查,但任何新增赋值/代码引用应改用上面的 DotnetLog*。
    /// <summary>deprecated:改用 <see cref="DotnetLog"/>。</summary>
    [Obsolete("Use DotnetLog (CTK_DOTNET_LOG); CTK_LOG 仅作 ReadEnvWithFallback 兜底。")]
    public const string Log = "CTK_LOG";
    /// <summary>deprecated:改用 <see cref="DotnetLogLevel"/>。</summary>
    [Obsolete("Use DotnetLogLevel (CTK_DOTNET_LOG_LEVEL); CTK_LOG_LEVEL 仅作兜底。")]
    public const string LogLevel = "CTK_LOG_LEVEL";
    /// <summary>deprecated:改用 <see cref="DotnetLogFormat"/>。</summary>
    [Obsolete("Use DotnetLogFormat (CTK_DOTNET_LOG_FORMAT); CTK_LOG_FORMAT 仅作兜底。")]
    public const string LogFormat = "CTK_LOG_FORMAT";
    /// <summary>deprecated:改用 <see cref="DotnetLogFile"/>。</summary>
    [Obsolete("Use DotnetLogFile (CTK_DOTNET_LOG_FILE); CTK_LOG_FILE 仅作兜底。")]
    public const string LogFile = "CTK_LOG_FILE";
    /// <summary>deprecated:改用 <see cref="DotnetLogRetentionDays"/>。</summary>
    [Obsolete("Use DotnetLogRetentionDays (CTK_DOTNET_LOG_RETENTION_DAYS); CTK_LOG_RETENTION_DAYS 仅作兜底。")]
    public const string LogRetentionDays = "CTK_LOG_RETENTION_DAYS";

    /// <summary>
    /// DiagnosticBundle 采集时纳入 manifest 的 CTK_* env vars 白名单。
    /// 新旧 .NET log env 全收录:迁移期任一边都可能被 user 设置,manifest 显示完整状态便于排查。
    /// </summary>
#pragma warning disable CS0618 // 引用 Obsolete 兜底名只为白名单收录,不构成新增使用
    public static readonly IReadOnlyList<string> All =
    [
        DotnetLog, DotnetLogLevel, DotnetLogFormat, DotnetLogFile, DotnetLogRetentionDays,
        Log, LogLevel, LogFormat, LogFile, LogRetentionDays,
        Env,
        HostManagedLog, HostNativeLog, HostMarkerLog,
        BootstrapLogDir, BootstrapLogRetentionDays, BootstrapTraceId,
        HostLogDir, HostLogRetentionDays,
        HostAssembly, HostRuntimeConfig, HostMethod,
        HostNativeDll,
        HostSmokeModel, HostSmokeModelType, HostPreloadModel, HostEnableModelOps,
        HostProtkDat, HostProtkDatDir,
        AppAssembly, AppType, AppLoadRibbon,
        AppPreloadModel, AppLoadModelPath,
        AppModelName, AppModelType,
        AppSystemMenuExtra, DispatchEcho, MessageBarMirror,
        AppMsgFile, AppRibbonFile,
        AppRunCommand, AppAllowedRoots,
        DiagnosticsEnabled, DiagnosticsRequired,
    ];
#pragma warning restore CS0618
}
