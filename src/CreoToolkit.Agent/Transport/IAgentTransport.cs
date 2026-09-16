using CreoToolkit.Agent.Commands;

namespace CreoToolkit.Agent.Transport;

public interface IAgentTransport
{
    /// <summary>执行 agent 命令。线程契约:当前实现均为同步路由,须在 Creo 主线程调用
    /// (native 调用受 dispatcher 主线程断言守门,后台线程调用会 fail-fast 抛出)。
    /// 异步 dispatcher 落地时由 transport 实现负责 marshal 到主线程,契约随之升级。</summary>
    Task<AgentResult> ExecuteAsync(AgentCommand command, CancellationToken ct = default);
}
