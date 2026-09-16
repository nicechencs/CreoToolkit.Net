namespace CreoToolkit.Agent.Commands;

/// <summary>Agent 派发 verb 的稳定错误信号:handler 抛出,router 映射到 AgentResult.Fail
/// 并把 <see cref="Error"/> 作为 error 分类、<see cref="Reason"/> 作为诊断消息。
/// <para>不复用 CreoException:CreoException 走 creo.* 映射(领域错误),
/// 本类走 agent.* 命名空间(agent 编排层错误)。</para></summary>
public sealed class AgentDispatchException : Exception
{
    /// <summary>稳定 error 分类字符串(如 agent.command-invoker-missing / agent.command-not-allowed)。</summary>
    public string Error { get; }

    /// <summary>诊断消息(供 AgentResult.Reason,面向排查)。</summary>
    public string Reason { get; }

    public AgentDispatchException(string error, string reason)
        : base($"{error}: {reason}")
    {
        ThrowUtil.IfNullOrWhiteSpace(error);
        ThrowUtil.IfNullOrWhiteSpace(reason);
        Error = error;
        Reason = reason;
    }
}
