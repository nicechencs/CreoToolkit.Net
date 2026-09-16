using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;

namespace CreoToolkit.Agent.Verbs;

/// <summary>command.dispatch verb handler:按 commandId 派发已注册的应用命令。
/// <para>三层守门(policy verb 白名单 → invoker 存在 → commandId 白名单):
/// 后两层未过时抛 <see cref="AgentDispatchException"/>,router 映射到 AgentResult.Fail。</para></summary>
internal static class CommandDispatchHandler
{
    /// <summary>派发入口。invoker/allowlist 由 router 构造时闭包捕获传入。</summary>
    public static IReadOnlyDictionary<string, object?> Handle(
        CreoSession session,
        AgentCommand command,
        ICommandInvoker? invoker,
        ISet<string> allowedCommandIds)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);
        ThrowUtil.IfNull(allowedCommandIds);

        // 参数校验:commandId 缺失/非字符串走 AgentArgs.GetString(required) → CreoException BadInputs
        // → router 统一映射成 creo.badinputs,与其他 verb 一致
        var commandId = AgentArgs.GetString(command, "commandId", required: true)!;

        // 层 1:invoker 存在(宿主未注入 adapter 时的稳定错误)
        if (invoker is null)
        {
            throw new AgentDispatchException(
                "agent.command-invoker-missing",
                "Agent 未注入 ICommandInvoker;宿主需通过 CreoCommandRouter 构造函数注入 invoker");
        }

        // 层 2:commandId 白名单(防 command.dispatch 变成通用逃生舱)
        if (allowedCommandIds.Count == 0 || !allowedCommandIds.Contains(commandId))
        {
            throw new AgentDispatchException(
                "agent.command-not-allowed",
                $"命令 '{commandId}' 不在派发白名单;由宿主构造 router 时经 allowedCommandIds 显式开放");
        }

        // 层 3:实际派发。invoker 契约保证不抛,派发失败经 Status/Message 表达
        var result = invoker.Invoke(commandId);
        return result.Status switch
        {
            CommandInvocationStatus.Ok => new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["commandId"] = commandId,
                ["status"] = "ok",
            },
            CommandInvocationStatus.NotFound => throw new AgentDispatchException(
                "agent.command-not-found",
                result.Message ?? $"命令 '{commandId}' 未注册"),
            CommandInvocationStatus.Failed => throw new AgentDispatchException(
                "agent.command-failed",
                result.Message ?? $"命令 '{commandId}' 执行失败"),
            _ => throw new AgentDispatchException(
                "agent.command-failed", $"未知派发状态: {result.Status}"),
        };
    }
}
