namespace CreoToolkit.App;

/// <summary>
/// 命令 handler 的串行执行接缝。host 经 <see cref="SessionCommandDispatcher"/> 投到
/// Creo 主线程；脱 Creo 测试用记录式 dispatcher。App 自定义本接口而非依赖 SDK internal 调度抽象（F8）。
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>串行执行 <paramref name="action"/>（在约定的主线程上）。</summary>
    void Invoke(Action action);
}

/// <summary>宿主调度器：把 handler 投给 <c>CreoSession.RunOnMainThread</c> 串行执行。</summary>
internal sealed class SessionCommandDispatcher : ICommandDispatcher
{
    private readonly Action<Action> _run;

    public SessionCommandDispatcher(Action<Action> run)
        => _run = run ?? throw new ArgumentNullException(nameof(run));

    public void Invoke(Action action) => _run(action);
}
