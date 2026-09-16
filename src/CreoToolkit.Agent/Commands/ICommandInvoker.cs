namespace CreoToolkit.Agent.Commands;

/// <summary>宿主命令派发接缝:把 commandId 交给已注册的应用命令(如 CreoAppHost 的按名 dispatch),
/// Agent 层不引用 App。宿主/samples 提供适配实现。
/// <para>调用发生在主线程(pipe → executor Pump → router → handler → invoker),
/// 实现方不再二次 marshal。</para></summary>
public interface ICommandInvoker
{
    /// <summary>按命令 id 派发已注册命令。commandId 未命中或命令抛异常时返回相应 Status,不抛异常。</summary>
    CommandInvocationResult Invoke(string commandId);
}
