using System.Runtime.Versioning;
using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Policy;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Native;
using CreoToolkit.Interop.Diagnostics;
using global::Serilog;

namespace CreoToolkit.Agent.Transport;

/// <summary>pipe agent 服务组合体:router + executor + server 三对象统一生命周期。
/// <para><see cref="Start"/> 时可选挂 session 释放链:session Dispose 自动回收本服务,
/// 杜绝 listener/executor/router 脱离其应用 owner 后永生泄漏。</para></summary>
[SupportedOSPlatform("windows")]
public sealed class AgentPipeService : IDisposable
{
    private static readonly ILogger Log = CreoSerilog.ForContext("Agent", CreoLogLayer.L4);
    private readonly CreoSession _session;
    private readonly CreoCommandRouter _router;
    private readonly MainThreadAgentExecutor _executor;
    private readonly NamedPipeAgentServer _server;
    private readonly object _lifecycleGate = new();
    private IDisposable? _sessionTracker;
    private bool _started;
    private int _disposed;

    public AgentPipeService(CreoSession session, IAgentPolicy policy, string? pipeName = null)
        : this(session, policy, commandInvoker: null, allowedCommandIds: null, pipeName: pipeName)
    {
    }

    /// <summary>扩容构造:注入 <paramref name="commandInvoker"/> 后 command.dispatch verb 可派发已注册应用命令;
    /// <paramref name="allowedCommandIds"/> 为 command id 白名单。</summary>
    public AgentPipeService(
        CreoSession session,
        IAgentPolicy policy,
        ICommandInvoker? commandInvoker,
        ISet<string>? allowedCommandIds,
        string? pipeName = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ThrowUtil.IfNull(policy);
        _router = new CreoCommandRouter(session, policy, commandInvoker, allowedCommandIds);
        _executor = new MainThreadAgentExecutor(_router);
        _server = new NamedPipeAgentServer(_executor, _router.RegisteredVerbs, pipeName);
    }

    /// <summary>pipe 名。</summary>
    public string PipeName => _server.PipeName;

    /// <summary>执行器(宿主经 <see cref="MainThreadAgentExecutor.RunPumpLoop"/> 在主线程泵命令)。</summary>
    public MainThreadAgentExecutor Executor => _executor;

    /// <summary>服务端(宿主订阅 <see cref="NamedPipeAgentServer.ShutdownRequested"/> 退出泵)。</summary>
    public NamedPipeAgentServer Server => _server;

    /// <summary>启动监听;<paramref name="bindToSessionLifetime"/> 为 true 时挂 session 释放链,
    /// session Dispose 时自动回收本服务(主线程执行)。</summary>
    public void Start(bool bindToSessionLifetime = false)
    {
        lock (_lifecycleGate)
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(AgentPipeService));
            if (_started)
                throw new InvalidOperationException("Agent pipe service has already been started.");

            _started = true;
            try
            {
                _server.Start();
                if (bindToSessionLifetime)
                    _sessionTracker = _session.TrackResource(new ResourceAdapter(this));
            }
            catch
            {
                // A listener may already have allocated its thread/CTS before tracker setup fails.
                // Tear down the whole service so partial initialization cannot escape this owner.
                _started = false;
                try { DisposeCore(); }
                catch { /* preserve the original Start failure */ }
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
            DisposeCore();
    }

    private void DisposeCore()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Every component owns potentially session-related state. Continue after each failure;
        // in particular a server/listener failure must not retain the router or executor.
        try { _server.Dispose(); }
        catch (Exception ex) { SafeWarn("关闭 pipe server 失败", ex); }
        try { _executor.Dispose(); }
        catch (Exception ex) { SafeWarn("关闭 agent executor 失败", ex); }
        try { _router.Dispose(); }
        catch (Exception ex) { SafeWarn("关闭 agent router 失败", ex); }

        // 主动 Dispose 时解除 session 跟踪;session 链触发时此调用为幂等 no-op。
        var tracker = _sessionTracker;
        _sessionTracker = null;
        try { tracker?.Dispose(); }
        catch (Exception ex) { SafeWarn("解除 session resource tracker 失败", ex); }
    }

    private static void SafeWarn(string message, Exception ex)
    {
        try
        {
            CreoSerilog.Warning(Log, message,
                @event: "pipe.cleanup-failed", props: new { error = ex.Message });
        }
        catch { /* logging must not abort later cleanup */ }
    }

    /// <summary>session TrackResource 适配:session Dispose → 回收整个服务。</summary>
    private sealed class ResourceAdapter : INativeResource
    {
        private readonly AgentPipeService _owner;

        public ResourceAdapter(AgentPipeService owner) => _owner = owner;

        public bool IsDisposed => Volatile.Read(ref _owner._disposed) != 0;

        public void Dispose() => _owner.Dispose();
    }
}
