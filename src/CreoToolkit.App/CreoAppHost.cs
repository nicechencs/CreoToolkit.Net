using CreoToolkit.App.Errors;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// 应用宿主：把声明的命令注册到 native 命令桥，提供单一 dispatch 路由（token→command→handler，
/// 经 <see cref="ICommandDispatcher"/> 串行 + try/catch 边界，D1/D2），并暴露 last-failed-token 可观测。
/// 约束：<see cref="CommandDispatchPin"/> 钉住回调、dispatcher 串行、必须在 Creo 主线程（host 经 CreoSession.RunOnMainThread）。
/// 托管异常一律在内部 dispatch 边界兜住，绝不穿回 native（native SEH 兜不住托管异常）。
/// </summary>
public sealed class CreoAppHost : IDisposable
{
    private readonly ICreoApplication _app;
    private readonly CreoSession? _session;
    private readonly ICommandBridge _bridge;
    private readonly ICommandDispatcher _dispatcher;
    private readonly string _msgFile;
    private readonly CallbackRegistry _registry = new();
    private readonly CommandDispatchRuntime _dispatchRuntime;
    private readonly MenuRegistrationRuntime _menuRegistrationRuntime;

    private const int ProFileNameMax = 39;

    private IReadOnlyList<CreoCommand> _cmds = Array.Empty<CreoCommand>();
    private CommandDispatchPin? _pin;
    // 0=usable, 1=Dispose executing, 2=completed. A failed native terminate
    // restores 0 so the still-pinned bridge can be retried safely.
    private int _disposeState;
    private bool _runStarted;
    private readonly int _ownerThreadId;
    // 菜单点击回显:handler 完成后追加 done 一行,失败时追加 FAILED 一行。
    // 默认开;env CTK_DISPATCH_ECHO=0/off/false 关闭(prod 场景或被 noisy 时)。
    private readonly bool _dispatchEcho = ResolveDispatchEchoFromEnv();
    private readonly bool _messageBarMirror = ResolveMessageBarMirrorFromEnv();
    // 配对契约标记：仅 OnInitialize 完整成功后置位，Dispose 据此决定是否调 OnTerminate。
    // 此标记置 true 时 _session 必非 null——Run() 仅在 `_session is not null` 分支才置位，
    // 故 Dispose 内的 _session! null-forgiving 安全。
    private bool _lifecycleInitialized;

    /// <summary>最近一次失败回调的 token（路由未命中或 handler 抛异常）；无失败为 -1。</summary>
    public int LastFailedToken => _dispatchRuntime.LastFailedToken;

    private CreoAppHost(ICreoApplication app, CreoSession? session, ICommandBridge bridge,
        ICommandDispatcher dispatcher, string msgFile)
    {
        _app = app ?? throw new ArgumentNullException(nameof(app));
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ValidateMsgFile(msgFile);
        _msgFile = msgFile ?? "";
        _session = session;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _dispatchRuntime = new CommandDispatchRuntime(
            () => _cmds,
            _session,
            _dispatcher,
            CreateMessages,
            _dispatchEcho);
        _menuRegistrationRuntime = new MenuRegistrationRuntime(
            _bridge, _dispatcher, _session, CreateMessages, _msgFile);
    }

    /// <summary>测试工厂：注入全部接缝。session 默认 null（路由测试不访问 ctx.Session，F10）。</summary>
    public static CreoAppHost ForTest(ICreoApplication app, ICommandBridge bridge,
        ICommandDispatcher dispatcher, string msgFile,
        CreoSession? session = null)
        => new(app, session, bridge, dispatcher, msgFile);

    /// <summary>宿主工厂：组真实 native 桥 + 经 CreoSession 主线程串行的 dispatcher。</summary>
    public static CreoAppHost ForHost(ICreoApplication app, CreoSession session, string msgFile)
    {
        ThrowUtil.IfNull(session);
        return new CreoAppHost(app, session, new NativeCommandBridge(),
            new SessionCommandDispatcher(session.RunOnMainThread), msgFile);
    }

    /// <summary>初始化 app、钉住单一 dispatch、注册并 designate 每条命令,然后挂载菜单按钮。</summary>
    public void Run()
    {
        ThrowIfWrongThread(nameof(Run));
        if (Volatile.Read(ref _disposeState) != 0)
            throw new ObjectDisposedException(nameof(CreoAppHost));
        if (_runStarted)
            throw new InvalidOperationException("CreoAppHost.Run can only be attempted once; dispose the host after a failed attempt.");
        _runStarted = true;
        CreoLog.Info("host.Run 开始", @event: "host.init.begin", module: "CreoAppHost", layer: CreoLogLayer.App);

        // 启动期 env 诊断:business 输出 mirror/echo 行为依赖这两个 env,排查时一行说清生效情况。
        CreoLog.Info(
            $"env {CtkEnv.DispatchEcho}='{Environment.GetEnvironmentVariable(CtkEnv.DispatchEcho) ?? "(unset)"}' -> _dispatchEcho={_dispatchEcho}; " +
            $"{CtkEnv.MessageBarMirror}='{Environment.GetEnvironmentVariable(CtkEnv.MessageBarMirror) ?? "(unset)"}' -> _messageBarMirror={_messageBarMirror}",
            @event: "host.env.resolved", module: "CreoAppHost", layer: CreoLogLayer.App,
            props: new
            {
                CTK_DISPATCH_ECHO = Environment.GetEnvironmentVariable(CtkEnv.DispatchEcho),
                CTK_MESSAGEBAR_MIRROR = Environment.GetEnvironmentVariable(CtkEnv.MessageBarMirror),
                dispatchEcho = _dispatchEcho,
                messageBarMirror = _messageBarMirror,
            });

        var builder = new CreoAppBuilder();
        _app.Initialize(builder);
        var definition = builder.BuildDefinition();   // ★ 一次性拿命令 + 菜单声明快照(校验菜单 commandName 存在)
        _cmds = definition.Commands;

        CtkManagedCommandDispatch dispatch = _dispatchRuntime.Dispatch;
        _pin = new CommandDispatchPin(_registry, dispatch);          // 钉住单一 native dispatch
        Check(_bridge.BridgeInitialize(_pin.FunctionPointer), "BridgeInitialize");

        // NativeCommandBridge implements this optional contract. Fakes and
        // downstream ICommandBridge implementations remain source-compatible;
        // legacy native callers preallocate one slot before each registration.
        if (_bridge is ICommandBridgeCapacity capacity)
            Check(capacity.CommandCapacityReserve(_cmds.Count), "CommandCapacityReserve");

        // 命令注册是硬失败阶段；菜单外观是后续可降级阶段。
        var commandIds = CommandRegistrationRuntime.Register(_cmds, _bridge, _msgFile);

        _menuRegistrationRuntime.Register(definition, commandIds);

        // 命令与菜单注册全部成功后，无条件调 lifecycle 钩子。
        // session=null 时（ForTest 默认）跳过——视为测试便利。
        // 抛异常将让整个 Run() 失败，Bootstrap.cs catch 走 DisposeManagedHost() → Dispose()。
        // Dispose 据 _lifecycleInitialized=false 跳 OnTerminate（配对契约）。
        if (_session is not null)
        {
            var msgs = CreateMessages(null);
            var ctx = new CreoAppContext(_session, msgs, _msgFile, this);
            CreoLog.Info($"lifecycle.OnInitialize 开始 app={_app.GetType().FullName}",
                @event: "lifecycle.init.begin", module: "CreoAppHost", layer: CreoLogLayer.App);
            _app.OnInitialize(ctx);
            _lifecycleInitialized = true;
            CreoLog.Info($"lifecycle.OnInitialize 完成 app={_app.GetType().FullName}",
                @event: "lifecycle.init.ok", module: "CreoAppHost", layer: CreoLogLayer.App);
        }
        else
        {
            CreoLog.Info("lifecycle 路径跳过（session=null，ForTest 便利）",
                @event: "lifecycle.skip", module: "CreoAppHost", layer: CreoLogLayer.App);
        }

        CreoLog.Info("host.Run 完成", @event: "host.init.ok", module: "CreoAppHost", layer: CreoLogLayer.App);
    }

    /// <summary>启动诊断用：按命令名走同一 dispatch/handler 路径，便于真实 Creo 启动阶段自动验收。
    /// <para>与 <see cref="InvokeCommand"/> 共享实现;保留 internal 名字维持 Bootstrap/Tests 契约。</para></summary>
    internal int RunCommandForDiagnostics(string name) => _dispatchRuntime.InvokeByName(name);

    /// <summary>按名派发已注册命令(与 diagnostic 同路径),返回 rc:
    /// 0=成功 / 1=handler 抛异常(<see cref="LastFailedToken"/> 记录 token) / 2=命令未注册。
    /// <para>internal:public 面只暴露 <see cref="CreoAppContext.InvokeCommand"/> 窄方法,
    /// 避免 host 整包(Run / Dispose 生命周期控制)被 ICreoApplication 实现触达。</para></summary>
    internal int InvokeCommand(string name) => _dispatchRuntime.InvokeByName(name);

    /// <summary>使用当前 host 已持有的 bridge 加载 ribbon，避免启动路径额外构造未释放的 bridge。</summary>
    public int LoadRibbon(string ribbonFile) => LoadRibbon(_bridge, ribbonFile);

    /// <summary>加载 ribbon 定义（可见性加分路径，失败仅记录不抛）。</summary>
    public static int LoadRibbon(ICommandBridge bridge, string ribbonFile)
    {
        ThrowUtil.IfNull(bridge);
        int rc = bridge.RibbonDefinitionfileLoad(ribbonFile);
        if (rc != 0)
        {
            CreoLog.Warn($"ribbon 加载失败 file={ribbonFile}",
                @event: "host.preload.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { file = ribbonFile, rc });
        }
        return rc;
    }

    /// <summary>解析 env CTK_DISPATCH_ECHO:未设/其他值 → true;0/off/false/no(忽略大小写) → false。</summary>
    private static bool ResolveDispatchEchoFromEnv()
    {
        var v = Environment.GetEnvironmentVariable(CtkEnv.DispatchEcho);
        if (string.IsNullOrWhiteSpace(v)) return true;
        return v.Trim().ToLowerInvariant() switch
        {
            "0" or "off" or "false" or "no" => false,
            _ => true,
        };
    }

    /// <summary>解析 env CTK_MESSAGEBAR_MIRROR:on/1/true/yes(忽略大小写) -> true;其他 -> false。
    /// default off: business 输出复刻量大,只在 dev 排查时显式开。</summary>
    private static bool ResolveMessageBarMirrorFromEnv()
    {
        var v = Environment.GetEnvironmentVariable(CtkEnv.MessageBarMirror);
        if (string.IsNullOrWhiteSpace(v)) return false;
        return v.Trim().ToLowerInvariant() switch
        {
            "1" or "on" or "true" or "yes" => true,
            _ => false,
        };
    }

    /// <summary>构造 CreoMessages;_messageBarMirror=true 时把 ctx.Messages.Info(text) 复刻到 CreoLog,
    /// 日志带 props.cmd=cmdName 便于排查时定位业务输出归属。mirror 失败吞异常 + Warn 留痕,绝不影响 native bridge 调用。</summary>
    private CreoMessages CreateMessages(string? cmdName)
    {
        if (!_messageBarMirror)
            return new CreoMessages(_msgFile, (file, text) => _bridge.MessageDisplay(file, text));

        return new CreoMessages(_msgFile, (file, text) =>
        {
            try
            {
                try
                {
                    CreoLog.Info(text,
                        @event: "messagebar.mirror.ok", module: "MessageBar", layer: CreoLogLayer.App,
                        props: cmdName is null ? null : new { cmd = cmdName });
                }
                catch (Exception ex)
                {
                    CreoLog.Warn("messagebar mirror 失败",
                        @event: "messagebar.mirror.fail", module: "MessageBar", layer: CreoLogLayer.App,
                        props: cmdName is null ? null : new { cmd = cmdName }, err: CreoLog.WithError(ex));
                }
            }
            catch
            {
                // 镜像日志不得阻塞 native message bar。
            }

            _bridge.MessageDisplay(file, text);
        });
    }

    /// <summary>卸载顺序:lifecycle.OnTerminate(可选) → bridge.Dispose(先令 command dispatch inert)
    /// → pin 解钉 → registry 释放。callback 内重入或错误线程会快速失败且保留 pin，供正确线程稍后重试。</summary>
    public void Dispose()
    {
        // Validate before changing disposal state so a wrong-thread attempt can
        // be corrected and retried on the Creo initialize thread.
        ThrowIfWrongThread(nameof(Dispose));
        if (_dispatchRuntime.IsDispatching)
            throw new InvalidOperationException(
                "CreoAppHost.Dispose cannot run from a command callback; return from the callback and dispose on the initialize thread.");

        if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            return;

        var bridgeTerminated = false;
        try
        {
            // lifecycle OnTerminate runs while the session remains valid. It is
            // marked complete before retryable bridge shutdown, preventing a
            // failed shutdown retry from calling the lifecycle hook twice.
            if (_lifecycleInitialized)
            {
                _lifecycleInitialized = false;
                var msgs = CreateMessages(null);
                var ctx = new CreoAppContext(_session!, msgs, _msgFile, this);
                Safe(() => _app.OnTerminate(ctx), "lifecycle.OnTerminate");
            }

            // NativeCommandBridge refuses a reentrant / wrong-thread terminate.
            // Do not unpin the delegate or clear command closures unless this
            // hand-off succeeds.
            try
            {
                _bridge.Dispose();
                bridgeTerminated = true;
            }
            catch (Exception ex)
            {
                SafeLogDisposeFailure("bridge.Dispose", ex);
                throw;
            }

            Safe(() => _pin?.Dispose(), "pin.Dispose");
            Safe(() => _registry.Dispose(), "registry.Dispose");
            _cmds = Array.Empty<CreoCommand>();
        }
        finally
        {
            Volatile.Write(ref _disposeState, bridgeTerminated ? 2 : 0);
        }
    }

    // bridge rc != 0 必须抛,避免 native user_initialize silent 返回 0。
    private static void Check(int rc, string operation)
    {
        if (rc != 0)
            throw new CreoBridgeException($"{operation} failed rc={rc}");
    }

    private static void ValidateMsgFile(string? msgFile)
    {
        if (msgFile is null)
            return;

        if (msgFile.Length > ProFileNameMax || msgFile.Any(c => c > 0x7f))
            throw new ArgumentException($"msgFile must be <= {ProFileNameMax} ASCII chars", nameof(msgFile));
    }

    private void Safe(Action action, string what)
    {
        try { action(); }
        catch (Exception ex)
        {
            SafeLogDisposeFailure(what, ex);
        }
    }

    private void ThrowIfWrongThread(string operation)
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException(
                $"CreoAppHost.{operation} must run on the thread that created the host.");
    }

    private static void SafeLogDisposeFailure(string what, Exception ex)
    {
        try
        {
            CreoLog.Error($"dispose 步骤失败 step={what}",
                @event: "command.dispose.step.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { step = what }, err: CreoLog.WithError(ex));
        }
        catch
        {
            // A logger failure must not turn cleanup into an unmanaged callback
            // exception or hide the native-root safety decision above.
        }
    }
}
