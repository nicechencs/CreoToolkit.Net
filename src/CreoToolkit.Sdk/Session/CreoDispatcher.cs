namespace CreoToolkit.Sdk.Session;

/// <summary>
/// 将委托调度到 Creo 主线程执行，确保所有 TOOLKIT 调用满足 Creo 单线程约束。
/// 被调委托抛出的异常原样传播给调用方。
/// </summary>
/// <remarks>
/// internal：暂不开放用户自定义调度；若日后需支持进程外/自定义 dispatcher，再经明确的 public 扩展点暴露。
/// </remarks>
internal interface ICreoDispatcher
{
    /// <summary>在 Creo 主线程执行 <paramref name="func"/> 并返回结果。</summary>
    T Invoke<T>(Func<T> func);

    /// <summary>在 Creo 主线程执行 <paramref name="action"/>。</summary>
    void Invoke(Action action);
}

/// <summary>
/// 同步调度器：在调用线程上直接执行委托。调用方已是 Creo 主线程时行为正确。
/// 已捕获主线程后若从其他线程调用，<see cref="AssertMainThread"/> 直接抛出；未捕获（注入式单测）则放行。
/// </summary>
internal sealed class SynchronousCreoDispatcher : ICreoDispatcher
{
    /// <inheritdoc />
    public T Invoke<T>(Func<T> func)
    {
        ThrowUtil.IfNull(func);
        AssertMainThread();
        return func(); // 异常原样传播
    }

    /// <inheritdoc />
    public void Invoke(Action action)
    {
        ThrowUtil.IfNull(action);
        AssertMainThread();
        action(); // 异常原样传播
    }

    // 未 Attach（MainThreadId 为 null）或单元测试显式传 dispatcher 时放行；已 Attach 则必须在已捕获的主线程调用。
    private static void AssertMainThread()
    {
        if (CreoThread.MainThreadId is int && !CreoThread.IsMainThread)
            throw new InvalidOperationException(
                "Creo SDK 调用必须在 Creo 主线程执行(同步拓扑); 检测到从非主线程调用。");
    }
}
