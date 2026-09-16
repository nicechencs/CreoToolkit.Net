using System.Runtime.Versioning;
using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Policy;
using CreoToolkit.Agent.Transport;
using CreoToolkit.App;

namespace CreoToolkit.Samples.AgentDemo;

public static class AgentDemoRegistration
{
    private const string LogModule = "AgentDemo";

    /// <summary>
    /// Registers this module against the runtime explicitly owned by the aggregate app.
    /// The generated catalog passes this instance through; no static registration-time
    /// handoff is retained between applications.
    /// </summary>
    public static void Register(CreoAppBuilder app, AgentDemoRuntime runtime)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNull(runtime);

        app.Command("pt.agent.demo", "PT_AGENT_DEMO", "PT_AGENT_DEMO_HELP", ctx =>
        {
            // marker:进入路径(ctx.Messages.Info 不写 managed log)。
            CreoAppLog.Info(
                $"pt.agent.demo handler entered, session={(ctx.Session is null ? "null" : "non-null")}",
                @event: "sample.enter",
                module: LogModule,
                props: new { sessionNull = ctx.Session is null });

            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_AGENT_DEMO_NO_SESSION");
                CreoAppLog.Info("session is null, exit early",
                    @event: "sample.no-session", module: LogModule);
                return;
            }

            var policy = new ReadOnlyAllowPolicy();
            using var router = new CreoCommandRouter(ctx.Session, policy);
            var result = router.Route(AgentCommand.Of("session.info"));

            // marker:结果 ok=true + attached=true。
            CreoAppLog.Info(
                $"agent session.info result: ok={result.Ok}",
                @event: "sample.result",
                module: LogModule,
                props: new { ok = result.Ok, error = result.Error, attached = result.Data?["attached"] });

            ctx.Messages.Info(result.Ok
                ? $"agent session.info result: attached={result.Data?["attached"]}"
                : $"agent denied/failed: {result.Error} - {result.Reason}");
        });

        // 写 verb 手测 — parameter.set 经 allow-list 注入开放
        app.Command("pt.agent.parameter-set-test", "PT_AGENT_PARAM_SET_TEST", "PT_AGENT_PARAM_SET_TEST_HELP", ctx =>
        {
            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_AGENT_DEMO_NO_SESSION");
                return;
            }
            using var router = new CreoCommandRouter(ctx.Session, new ReadOnlyAllowPolicy(["parameter.set"]));
            var cmd = AgentCommand.OfWithArgs(
                "parameter.set",
                ("name", "CTK_AGENT_TEST"),
                ("kind", "string"),
                ("value", "hello")).WithCorrelation("ui-test");
            var result = router.Route(cmd);
            CreoAppLog.Info(
                $"agent parameter.set result: ok={result.Ok}",
                @event: "sample.result",
                module: LogModule,
                props: new { ok = result.Ok, error = result.Error, verb = "parameter.set" });
            ctx.Messages.Info(result.Ok
                ? $"parameter.set ok: name={result.Data?["name"]} kind={result.Data?["kind"]}"
                : $"parameter.set failed: {result.Error} - {result.Reason}");
        });

        // 写 verb 手测 — model.save 空白名单 expect deny
        app.Command("pt.agent.model-save-deny-test", "PT_AGENT_MODEL_SAVE_DENY_TEST", "PT_AGENT_MODEL_SAVE_DENY_TEST_HELP", ctx =>
        {
            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_AGENT_DEMO_NO_SESSION");
                return;
            }
            using var router = new CreoCommandRouter(ctx.Session, new ReadOnlyAllowPolicy(allowedWriteVerbs: null));
            var result = router.Route(AgentCommand.Of("model.save").WithCorrelation("ui-deny-test"));
            CreoAppLog.Info(
                $"agent model.save deny result: ok={result.Ok}",
                @event: "sample.result",
                module: LogModule,
                props: new { ok = result.Ok, error = result.Error, verb = "model.save" });
            ctx.Messages.Info(result.Ok
                ? "model.save unexpectedly allowed"
                : $"model.save denied: {result.Error} - {result.Reason}");
        });

        // pipe server: 显式进入有限的主线程泵窗口，处理进程外 agent 命令。
        // listener 会在窗口结束后继续监听；下一次点击可再开一个窗口。
        app.Command("pt.agent.pipe-server", "PT_AGENT_PIPE_SERVER", "PT_AGENT_PIPE_SERVER_HELP", ctx =>
        {
            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_AGENT_DEMO_NO_SESSION");
                return;
            }

            runtime.RunPipeServerWindow(ctx.Session, ctx.Messages);
        });

        // selection token 全链路 sample — pick → highlight → release
        app.Command("pt.agent.selection-pick-highlight-release-test",
            "PT_AGENT_SELECTION_PHR_TEST", "PT_AGENT_SELECTION_PHR_TEST_HELP", ctx =>
        {
            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_AGENT_DEMO_NO_SESSION");
                return;
            }
            using var router = new CreoCommandRouter(ctx.Session,
                new ReadOnlyAllowPolicy(["selection.pick-programmatic", "selection.highlight", "selection.release"]));

            var pick = router.Route(AgentCommand.OfWithArgs(
                "selection.pick-programmatic",
                ("itemRefs", Array.Empty<string>())).WithCorrelation("phr-pick"));
            if (!pick.Ok)
            {
                ctx.Messages.Info($"pick failed: {pick.Error} - {pick.Reason}");
                CreoAppLog.Info($"phr pick failed: {pick.Error}",
                    @event: "sample.result", module: LogModule,
                    props: new { stage = "pick", ok = false, error = pick.Error });
                return;
            }
            var token = pick.Data?["token"];

            var hi = router.Route(AgentCommand.OfWithArgs(
                "selection.highlight",
                ("token", token!),
                ("index", 0)).WithCorrelation("phr-highlight"));
            var rel = router.Route(AgentCommand.OfWithArgs(
                "selection.release",
                ("token", token!)).WithCorrelation("phr-release"));

            CreoAppLog.Info(
                $"phr full chain: pick.ok={pick.Ok} highlight.ok={hi.Ok} release.ok={rel.Ok}",
                @event: "sample.result", module: LogModule,
                props: new { stage = "phr-end", pick = pick.Ok, highlight = hi.Ok, release = rel.Ok });

            ctx.Messages.Info($"PHR done: pick={pick.Ok} highlight={hi.Ok} release={rel.Ok}");
        });

    }

    /// <summary>command.dispatch 白名单 command id 集:仅允许 post-init dispatch 派发这些命令。
    /// <para>首要用例 pt.drawing.fixture-create — 让外部进程在 Creo UI 就绪后触发 fixture 生成,
    /// 绕开 init 上下文的 draft entity 边界(R16-R18)。</para>
    /// <para>扩此集需评估安全性:每加一条 = 外部可从 pipe 触发一条应用命令。</para></summary>
    internal static readonly HashSet<string> AllowedDispatchCommandIds = new(StringComparer.Ordinal)
    {
        "pt.drawing.fixture-create",
        "pt.drawing.dtlentity-curve-data",
        "pt.drawing.list-views",
        "pt.drawing.list-tables",
        "pt.drawing.list-detail-notes",
        "pt.drawing.list-dtlgroups",
        // select-probe 是交互式诊断探针:dispatch 后 ProSelect 阻塞主线程等真人在
        // Creo UI 点选(middle-click 结束)——设计用途即"agent 发起+人配合"取证
        // (2026-07-07 W1 工作流实证);纯自动化 client 不应派它,派了会挂到有人操作
        "pt.drawing.select-probe",
    };

}

/// <summary>
/// 单个聚合应用拥有的 Agent 生命周期对象。它在 <c>OnInitialize</c> 绑定 host invoker，
/// 在 <c>OnTerminate</c> 停止 listener 并清除所有 session 相关引用。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AgentDemoRuntime : IDisposable
{
    private readonly object _gate = new();
    private AgentPipeService? _pipeService;
    private Func<string, int>? _commandDispatch;
    private bool _initialized;
    private bool _disposed;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _pumpWindowActive;

    /// <summary>绑定 host。CTK_AGENT_PIPE=1 可启动 listener,但绝不自动进入主线程泵。</summary>
    public void Initialize(CreoAppContext ctx)
    {
        ThrowUtil.IfNull(ctx);
        AssertOwnerThread();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (_initialized)
                throw new InvalidOperationException("Agent demo runtime is already initialized.");

            _commandDispatch = ctx.InvokeCommand;
            _initialized = true;
            try
            {
                if (Environment.GetEnvironmentVariable("CTK_AGENT_PIPE") == "1")
                {
                    var service = StartPipeListenerCore(ctx.Session);
                    SafeLogInfo($"pipe listener automatically started: {service.PipeName}", "pipe.auto-start",
                        new { pipeName = service.PipeName, pumpActive = false });
                }
            }
            catch
            {
                DisposePipeServiceCore();
                _commandDispatch = null;
                _initialized = false;
                throw;
            }
        }
    }

    /// <summary>
    /// Runs the <c>pt.agent.pipe-server</c> explicit main-thread pump for a fixed ten-second
    /// window. The listener is started on first use and remains alive after the window; a later
    /// command opens another window. <c>agent.shutdown</c> only ends this window. The deadline
    /// is checked while waiting and between queued commands; an already-running native Pro* call
    /// is intentionally not preempted.
    /// </summary>
    public void RunPipeServerWindow(CreoToolkit.Sdk.CreoSession session, CreoMessages messages)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(messages);
        AssertOwnerThread();

        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_initialized || _commandDispatch is null)
                throw new InvalidOperationException("Agent demo runtime is not initialized with a host command invoker.");
            if (_pumpWindowActive)
                throw new InvalidOperationException("Agent main-thread pump window is already active.");

            var service = StartPipeListenerCore(session);
            using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            void StopPump()
            {
                try { stopping.Cancel(); }
                catch (ObjectDisposedException) { /* A background event snapshot may outlive unsubscription. */ }
            }
            service.Server.ShutdownRequested += StopPump;
            _pumpWindowActive = true;
            try
            {
                SafeLogInfo($"pipe main-thread pump started: {service.PipeName}", "pipe.manual-pump-start",
                    new { pipeName = service.PipeName, windowSeconds = 10 });
                messages.Info($"Agent pipe 已监听: {service.PipeName}; 主线程处理窗口为 10 秒");
                service.Executor.RunPumpLoop(stopping.Token, maxItemsPerIteration: 8);
            }
            finally
            {
                _pumpWindowActive = false;
                service.Server.ShutdownRequested -= StopPump;
                try { messages.Info("Agent pipe 主线程处理窗口已结束；listener 仍保持监听，可再次执行此命令。"); }
                catch (Exception ex) { SafeLogWarn("报告 pipe pump 结束状态失败", ex); }
                SafeLogInfo("pipe main-thread pump ended", "pipe.manual-pump-end",
                    new { pipeName = service.PipeName, shutdownRequested = stopping.IsCancellationRequested });
            }
        }
    }

    public void Dispose()
    {
        AssertOwnerThread();
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                DisposePipeServiceCore();
            }
            finally
            {
                _commandDispatch = null;
                _initialized = false;
            }
        }
    }

    private AgentPipeService StartPipeListenerCore(CreoToolkit.Sdk.CreoSession session)
    {
        if (_pipeService is { } existing)
            return existing;

        var policy = new ReadOnlyAllowPolicy([
            "parameter.set", "model.regenerate", "model.save",
            "selection.pick-programmatic", "selection.highlight", "selection.release",
            "command.dispatch"]);
        var invoker = new DelegateCommandInvoker(_commandDispatch
            ?? throw new InvalidOperationException("Agent command invoker is not bound."));
        var service = new AgentPipeService(session, policy, invoker, AgentDemoRegistration.AllowedDispatchCommandIds);
        try
        {
            // App runtime owns the service. Do not also add a session tracker root.
            service.Start();
            _pipeService = service;
            return service;
        }
        catch
        {
            try
            {
                service.Dispose();
            }
            catch (Exception ex)
            {
                SafeLogWarn("回滚 pipe listener 失败", ex);
            }
            throw;
        }
    }

    private void DisposePipeServiceCore()
    {
        var service = _pipeService;
        _pipeService = null;
        if (service is null)
            return;

        try
        {
            service.Dispose();
        }
        catch (Exception ex)
        {
            SafeLogWarn("关闭 pipe listener 失败", ex);
        }
    }

    private static void SafeLogInfo(string message, string eventName, object props)
    {
        try { CreoAppLog.Info(message, @event: eventName, module: "AgentDemo", props: props); }
        catch { /* cleanup must not be aborted by logging */ }
    }

    private static void SafeLogWarn(string message, Exception ex)
    {
        try
        {
            CreoAppLog.Warn(message, @event: "pipe.cleanup-failed", module: "AgentDemo",
                props: new { error = ex.Message });
        }
        catch { /* cleanup must not be aborted by logging */ }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(AgentDemoRuntime));
    }

    private void AssertOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("AgentDemoRuntime lifecycle must run on its application owner thread.");
    }
}
