using CreoToolkit.Agent.Commands;

namespace CreoToolkit.Samples.AgentDemo;

/// <summary>把「按名派发命令」委托(如 <c>CreoAppContext.InvokeCommand</c>)包装成 Agent 的
/// <see cref="ICommandInvoker"/>。不持 CreoAppHost 整包引用。
/// <para>rc 映射:0→Ok / 2→NotFound / 其它(1)→Failed。</para>
/// <para>不吞异常:派发路径的异常已在 host dispatch 边界 catch 并折成 rc=1,不会抛到本层。</para></summary>
internal sealed class DelegateCommandInvoker : ICommandInvoker
{
    private readonly Func<string, int> _dispatch;

    public DelegateCommandInvoker(Func<string, int> dispatch)
        => _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));

    public CommandInvocationResult Invoke(string commandId)
    {
        ThrowUtil.IfNullOrWhiteSpace(commandId);
        var rc = _dispatch(commandId);
        return rc switch
        {
            0 => CommandInvocationResult.Ok(),
            2 => CommandInvocationResult.NotFound(commandId),
            // rc=1 = handler 抛异常(dispatch 边界兜住);其余值防御性归 Failed
            _ => CommandInvocationResult.Failed($"命令 '{commandId}' 执行失败 rc={rc}"),
        };
    }
}
