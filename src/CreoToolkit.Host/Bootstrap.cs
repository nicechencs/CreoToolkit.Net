using System.Runtime.CompilerServices;
using System.Threading;
using CreoToolkit.App;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Sdk.Session;

namespace CreoToolkit.Host;

/// <summary>
/// 从同步 Creo TOOLKIT DLL 调入的托管宿主入口点。
/// CLR 4 <c>ExecuteInDefaultAppDomain</c> 要求 <c>public static int Method(string)</c>。
/// </summary>
public static class Bootstrap
{
    private static CreoSession? _session;
    private static CreoAppHost? _appHost;
    private static int _logFileBound;  // CreoLog.SetFile 幂等绑定标记
    // Bootstrap 入口/生命周期文本日志直接持 Serilog ILogger；需要稳定 event/props 的组件诊断仍走 CreoLog。
    // volatile 跨线程可见:EnsureLogFileBound 主线程一次性敲定,Log 可能从任意 Creo 回调线程进入。
    private static volatile global::Serilog.ILogger? _hostLogger;

    public static int Initialize(string? arg) => RunEntry("Initialize", disposeOnFailure: false, body: () => 0);

    public static int InitializeAndAttach(string? arg) => RunEntry("InitializeAndAttach", body: () =>
    {
        EnsureSession();
        Log("InitializeAndAttach: CreoSession.Attach succeeded.");
        return 0;
    });

    public static int InitializeApp(string? arg) => RunEntry("InitializeApp", body: () =>
    {
        // idempotency guard:Creo user_initialize 一进程调一次,但 protk.dat 错配 /
        // 测试 harness / 未来 reload 实验都可能触发重入。重入时拒绝优于静默覆盖 _appHost(避免
        // callback 泄漏 + cmd_id 路由错位)。
        if (_appHost is not null)
        {
            Log("InitializeApp: REJECTED - already initialized; duplicate call.");
            return -1;
        }

        var session = EnsureSession();
        Log("InitializeApp: CreoSession.Attach succeeded.");

        var diagnostics = DiagnosticsActivation.FromEnvironment();
        if (diagnostics.Requested)
        {
            var diagnosticsRc = TryPrepareDiagnostics(session, diagnostics.Required);
            if (diagnosticsRc != 0)
            {
                // Creo 对 user_initialize 非零通常不回调 Terminate;
                // 前置诊断分支若失败需与下方后置分支同步清理已建 _session,防泄漏。
                DisposeManagedHost();
                return diagnosticsRc;
            }
        }

        var options = HostStartupOptions.FromEnvironment();
        // 落地 env 解析结果:msg_file 错配是 ProCmdDesignate rc=-28 的常见根因,
        // 每次踩坑靠这条 log 一眼定位(source=default 时对齐 native text_dir 的 msg 文件真名)。
        var msgFileSource = options.MsgFileFromEnvironment ? "env" : "default";
        CreoLog.Info($"InitializeApp: msg_file={options.MsgFile} (source={msgFileSource}).",
            @event: "app.env.msg_file.resolved", module: "Host", layer: CreoLogLayer.App,
            props: new { msgFile = options.MsgFile, source = msgFileSource, envName = CtkEnv.AppMsgFile });

        var loaded = CreoApplicationLoader.CreateLoadedFromEnvironment(LogFromLoader);
        _appHost = CreoAppHost.ForHost(loaded.App, session, options.MsgFile);
        _appHost.Run();
        Log("InitializeApp: commands registered.");

        if (diagnostics.Requested)
        {
            var diagnosticsRc = TryRunAppDiagnostics(_appHost, diagnostics.Required);
            if (diagnosticsRc != 0)
            {
                DisposeManagedHost();
                return diagnosticsRc;
            }
        }

        return 0;
    });

    private static int TryPrepareDiagnostics(CreoSession session, bool required)
    {
        try
        {
            return InvokePrepareDiagnostics(session, required);
        }
        catch (Exception ex) when (IsDiagnosticsLoadFailure(ex))
        {
            Log($"InitializeApp: Diagnostics unavailable: {ex.Message}", CreoLogLevel.Error);
            return required ? -1 : 0;
        }
    }

    private static int TryRunAppDiagnostics(CreoAppHost appHost, bool required)
    {
        try
        {
            return InvokeRunAppDiagnostics(appHost, required);
        }
        catch (Exception ex) when (IsDiagnosticsLoadFailure(ex))
        {
            Log($"InitializeApp: Diagnostics unavailable: {ex.Message}", CreoLogLevel.Error);
            return required ? -1 : 0;
        }
    }

    // Keep optional assembly resolution behind non-inlined primitive-returning seams.
    // If Diagnostics.dll is absent, the outer try/catch remains JIT-able and normal app
    // loading continues unless CTK_DIAGNOSTICS_REQUIRED was explicitly enabled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InvokePrepareDiagnostics(CreoSession session, bool required)
        => CreoToolkit.Diagnostics.StartupDiagnostics.PrepareSession(
            session, required, (message, level) => Log(message, level));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InvokeRunAppDiagnostics(CreoAppHost appHost, bool required)
        => CreoToolkit.Diagnostics.StartupDiagnostics.RunAppActions(
            appHost, required, (message, level) => Log(message, level));

    private static bool IsDiagnosticsLoadFailure(Exception exception)
        => exception is FileNotFoundException or FileLoadException or TypeLoadException or BadImageFormatException
           || exception.InnerException is not null && IsDiagnosticsLoadFailure(exception.InnerException);

    public static int Terminate(string? arg) => RunEntry("Terminate", disposeOnFailure: false, body: () =>
    {
        if (!DisposeManagedHost())
            return -1;
        Log("Terminate: managed host terminated.");
        return 0;
    });

    /// <summary>
    /// Creo TOOLKIT 入口的通用样板:统一进入日志 + 异常吞捕 + 失败清理。
    /// <para><paramref name="disposeOnFailure"/> 默认 true:任何会构造 <c>_session/_appHost</c> 的入口失败时
    /// 必须释放已建资源,避免 Creo 拒绝回调 Terminate 时留泄漏;<c>Initialize</c> 与 <c>Terminate</c> 不创建/已经在
    /// 释放,无需再调。</para>
    /// </summary>
    private static int RunEntry(string name, Func<int> body, bool disposeOnFailure = true)
    {
        try
        {
            CreoApplicationLoader.EnsureSharedAssemblyResolve();
            Log($"{name}: managed host entered.");
            return body();
        }
        catch (Exception ex)
        {
            TryWriteInitFailure(name, ex);
            try { Log($"{name}: FAILED {ex}", CreoLogLevel.Error); } catch { }
            if (disposeOnFailure)
                DisposeManagedHost();
            return -1;
        }
    }

    /// <summary>
    /// Serilog 在 xtop 里可能尚未绑上文件 sink；把异常落到 native bootstrap 同目录，
    /// 避免 ExecuteInDefaultAppDomain 只回 -1、Creo 只显示 PRO_TK_GENERAL_ERROR。
    /// </summary>
    private static void TryWriteInitFailure(string name, Exception ex)
    {
        try
        {
            var dir = Environment.GetEnvironmentVariable(CtkEnv.BootstrapLogDir);
            if (string.IsNullOrWhiteSpace(dir))
                dir = Environment.GetEnvironmentVariable(CtkEnv.HostLogDir);
            if (string.IsNullOrWhiteSpace(dir))
                dir = Path.GetTempPath();
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "managed-init-failure.txt");
            File.WriteAllText(path,
                $"{DateTime.Now:o}{Environment.NewLine}" +
                $"entry={name}{Environment.NewLine}" +
                $"pid={System.Diagnostics.Process.GetCurrentProcess().Id}{Environment.NewLine}" +
                $"BaseDirectory={AppContext.BaseDirectory}{Environment.NewLine}" +
                $"HostLocation={typeof(Bootstrap).Assembly.Location}{Environment.NewLine}" +
                $"CTK_APP_ASSEMBLY={Environment.GetEnvironmentVariable(CtkEnv.AppAssembly)}{Environment.NewLine}" +
                $"CTK_APP_TYPE={Environment.GetEnvironmentVariable(CtkEnv.AppType)}{Environment.NewLine}" +
                $"CTK_APP_ALLOWED_ROOTS={Environment.GetEnvironmentVariable(CtkEnv.AppAllowedRoots)}{Environment.NewLine}" +
                $"{ex}{Environment.NewLine}");
        }
        catch
        {
            // 最后兜底失败也不能再抛，否则 ExecuteInDefaultAppDomain 连 -1 都回不去。
        }
    }

    // 失败路径清理:Creo 对 init 返回非零通常不回调 Terminate,故自行释放已构造的 host/session。
    private static bool DisposeManagedHost()
    {
        try
        {
            // Refusal (wrong thread / live callback / native terminate failure)
            // must retain both owners. Closing the Session underneath a still-
            // registered callback is not a successful cleanup.
            if (_session is not null)
                ThrowIfWrongCreoThread();
            _appHost?.Dispose();
        }
        catch (Exception ex)
        {
            TryLogDisposeFailure("appHost", ex);
            return false;
        }
        _appHost = null;
        try { _session?.Dispose(); }
        catch (Exception ex)
        {
            TryLogDisposeFailure("session", ex);
            return false;
        }
        _session = null;
        return true;
    }

    private static void TryLogDisposeFailure(string owner, Exception ex)
    {
        try { Log($"DisposeManagedHost: {owner} dispose failed {ex}", CreoLogLevel.Error); }
        catch { /* Logging must not change callback-root ownership. */ }
    }

    private static void ThrowIfWrongCreoThread()
    {
        if (CreoThread.MainThreadId is int && !CreoThread.IsMainThread)
            throw new InvalidOperationException("Managed host lifecycle must run on the Creo initialize thread.");
    }

    // 首次进入时把 CTK_HOST_MANAGED_LOG base path 绑到 CreoLog 文件 sink 作为
    // 兼容/配置绑定 —— DiagnosticBundle 与外部消费者仍按 base path 派生(见 FindRolledLogs)。
    // 实际写入走 CreoSerilog ILogger(复用 CreoLog 同一 Serilog 引擎,无文件锁分叉),
    // Bootstrap 自身不再直接调 CreoLog 静态门面。
    private static void EnsureLogFileBound()
    {
        if (Interlocked.Exchange(ref _logFileBound, 1) != 0) return;
        try
        {
            // RegisterDefault 抛异常必须走 catch 重置 _logFileBound 标记;
            // 否则文件日志轨永久卡在"已绑定"而实际未挂 sink,首个 Log 又把此异常当成入口失败。
            CreoTelemetryBootstrap.RegisterDefault();
            var path = Environment.GetEnvironmentVariable(CtkEnv.HostManagedLog);
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(AppContext.BaseDirectory, "logs", "host-managed.log");

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            CreoLog.SetFile(path);

            var logger = CreoSerilog.ForContext("Host", CreoLogLayer.App);
            _hostLogger = logger;

            // append 模式后同一天日志保留多次 session,写 session.start 边界,
            // 排查时 grep 'session.start' 直接定位本次启动起点。pid 区分多个并发 host(罕见,但准确)。
            CreoSerilog.Information(
                logger,
                "=== session.start pid={Pid} ===",
                propertyValues: new object?[] { System.Diagnostics.Process.GetCurrentProcess().Id },
                @event: "session.start",
                props: new
                {
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    machine = Environment.MachineName,
                    user = Environment.UserName,
                    cwd = Environment.CurrentDirectory,
                    bootstrapTraceId = Environment.GetEnvironmentVariable(CtkEnv.BootstrapTraceId),
                });
        }
        catch
        {
            // 文件 sink 绑定失败不致命(stderr 仍可观察);重置标记便于下次重试。
            Interlocked.Exchange(ref _logFileBound, 0);
        }
    }

    private static void Log(
        string message,
        CreoLogLevel level = CreoLogLevel.Info,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        EnsureLogFileBound();
        // EnsureLogFileBound 失败 → _hostLogger 仍 null;此处即时从 CreoSerilog 取一份(失败也只是 Logger.None 不抛)。
        var logger = _hostLogger ?? CreoSerilog.ForContext("Host", CreoLogLayer.App);
        var values = new object?[] { message };
        switch (level)
        {
            case CreoLogLevel.Error:
                CreoSerilog.Error(logger, "{Message}", propertyValues: values, file: file, line: line);
                break;
            case CreoLogLevel.Warn:
                CreoSerilog.Warning(logger, "{Message}", propertyValues: values, file: file, line: line);
                break;
            case CreoLogLevel.Trace:
                CreoSerilog.Verbose(logger, "{Message}", propertyValues: values, file: file, line: line);
                break;
            default:
                CreoSerilog.Information(logger, "{Message}", propertyValues: values, file: file, line: line);
                break;
        }
    }

    private static void LogFromLoader(string message)
        => Log(message, file: string.Empty, line: 0);

    private static CreoSession EnsureSession()
    {
        ThrowIfWrongCreoThread();
        CreoApplicationLoader.EnsureSharedAssemblyResolve();
        NativeHostModule.EnsureLoaded();
        return _session ??= CreoSession.Attach();
    }

}

