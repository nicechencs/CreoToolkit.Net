using System.Threading;
using CreoToolkit.Agent.Handles;
using CreoToolkit.Agent.Policy;
using CreoToolkit.Agent.Verbs;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;
using global::Serilog;

namespace CreoToolkit.Agent.Commands;

/// <summary>Verb router: policy check + handler dispatch + route audit.</summary>
public sealed class CreoCommandRouter : IDisposable
{
    private const string LogModule = "Agent";
    private const CreoLogLayer LogLayer = CreoLogLayer.L4;

    private static readonly ILogger Log = CreoSerilog.ForContext(LogModule, LogLayer);

    private static int _nextEpoch;

    private readonly CreoSession _session;
    private readonly IAgentPolicy _policy;
    private int _disposed;

    internal readonly HandleRegistry Handles;

    internal delegate IReadOnlyDictionary<string, object?> VerbHandler(
        CreoSession session,
        AgentCommand command,
        HandleRegistry registry);

    internal readonly Dictionary<string, VerbHandler> Handlers;

    /// <summary>已注册 verb 名清单(排序,零 I/O)。</summary>
    public IReadOnlyList<string> RegisteredVerbs { get; }

    public CreoCommandRouter(CreoSession session, IAgentPolicy policy)
        : this(session, policy, commandInvoker: null, allowedCommandIds: null)
    {
    }

    /// <summary>扩容构造:注入 <paramref name="commandInvoker"/> 后 command.dispatch verb 可派发已注册应用命令;
    /// <paramref name="allowedCommandIds"/> 为可派发的 command id 白名单(未命中集拒)。
    /// <para>invoker=null 时 command.dispatch 仍注册,handler 层返稳定错误 agent.command-invoker-missing。
    /// verb 层白名单仍由 policy 决定(command.dispatch 归写级别)。</para></summary>
    public CreoCommandRouter(
        CreoSession session,
        IAgentPolicy policy,
        ICommandInvoker? commandInvoker,
        ISet<string>? allowedCommandIds)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Handles = new HandleRegistry(Interlocked.Increment(ref _nextEpoch));
        // 白名单集合定型:null → 空集,handler 层统一命中判定
        var idSet = allowedCommandIds ?? new HashSet<string>(StringComparer.Ordinal);
        Handlers = new Dictionary<string, VerbHandler>(StringComparer.Ordinal)
        {
            // 读 verb:默认放行。
            ["session.info"] = WrapLegacy(SessionInfoHandler.Handle),
            ["model.summary"] = WrapLegacy(ModelSummaryHandler.Handle),
            // 写 verb:默认拒,由 ReadOnlyAllowPolicy 白名单显式开放。
            ["model.regenerate"] = WrapLegacy(ModelRegenerateHandler.Handle),
            ["parameter.set"] = WrapLegacy(ParameterSetHandler.Handle),
            ["model.save"] = WrapLegacy(ModelSaveHandler.Handle),
            ["selection.pick-programmatic"] = SelectionPickProgrammaticHandler.Handle,
            ["selection.highlight"] = SelectionHighlightHandler.Handle,
            ["selection.release"] = SelectionReleaseHandler.Handle,
            // command.dispatch:总是注册,invoker/allowlist 通过闭包传给 handler
            ["command.dispatch"] = (s, c, _) =>
                CommandDispatchHandler.Handle(s, c, commandInvoker, idSet),
        };
        RegisteredVerbs = Handlers.Keys.OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private static VerbHandler WrapLegacy(
        Func<CreoSession, AgentCommand, IReadOnlyDictionary<string, object?>> twoArg)
        => (session, command, _) => twoArg(session, command);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Handles.Dispose();
    }

    public AgentResult Route(AgentCommand command)
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(CreoCommandRouter));

        ThrowUtil.IfNull(command);

        using var corrScope = CreoSerilog.Scope(command.CorrelationId);

        // command.dispatch 的 commandId 进结构化 props(deny/fail/ok 全路径可按字段聚合,
        // 不再只藏在中文 reason 文本);其他 verb 恒为 null
        var commandId = TryExtractCommandId(command);

        var decision = _policy.Evaluate(command);
        if (!decision.Allow)
        {
            var result = AgentResult.Fail(
                command.CorrelationId,
                "policy.denied",
                decision.DenyReason ?? "policy denied");
            CreoSerilog.Information(
                Log,
                "agent route denied",
                @event: "route.deny",
                props: new { verb = command.Verb, commandId, error = result.Error, reason = result.Reason });
            return result;
        }

        if (!Handlers.TryGetValue(command.Verb, out var handler))
        {
            var result = AgentResult.Fail(
                command.CorrelationId,
                "verb.unknown",
                $"未知 verb: {command.Verb}");
            CreoSerilog.Information(
                Log,
                "agent verb unknown",
                @event: "route.fail",
                props: new { verb = command.Verb, commandId, error = result.Error });
            return result;
        }

        try
        {
            var data = handler(_session, command, Handles);
            CreoSerilog.Information(
                Log,
                "agent route ok",
                @event: "route.ok",
                props: new { verb = command.Verb, commandId });
            return AgentResult.Success(command.CorrelationId, data);
        }
        catch (AgentDispatchException ex)
        {
            // agent.* 命名空间错误(command.dispatch handler 抛):稳定分类不映射 creo.*
            var result = AgentResult.Fail(command.CorrelationId, ex.Error, ex.Reason);
            CreoSerilog.Information(
                Log,
                "agent route fail (dispatch)",
                @event: "route.fail",
                props: new { verb = command.Verb, commandId, error = result.Error, reason = result.Reason });
            return result;
        }
        catch (CreoException ex)
        {
            var result = AgentResult.Fail(
                command.CorrelationId,
                $"creo.{ex.ErrorCode.ToString().ToLowerInvariant()}",
                ex.Message);
            CreoSerilog.Error(
                Log,
                ex,
                "agent route fail (creo)",
                @event: "route.fail",
                props: new { verb = command.Verb, commandId, error = result.Error });
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = AgentResult.Fail(
                command.CorrelationId,
                "agent.unexpected",
                $"{ex.GetType().Name}: {ex.Message}");
            CreoSerilog.Error(
                Log,
                ex,
                "agent route unexpected",
                @event: "route.fail",
                props: new { verb = command.Verb, commandId, error = result.Error });
            return result;
        }
    }

    /// <summary>best-effort 从 command.dispatch args 抽 commandId 供审计 props;
    /// 非该 verb / 参数缺失 / 非字符串一律返 null(不抛,参数校验归 handler 层)。</summary>
    private static string? TryExtractCommandId(AgentCommand command)
    {
        if (!string.Equals(command.Verb, "command.dispatch", StringComparison.Ordinal))
            return null;
        if (!command.Args.TryGetValue("commandId", out var value) || value is null)
            return null;
        return value switch
        {
            string s => s,
            System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.String
                => je.GetString(),
            _ => null,
        };
    }
}
