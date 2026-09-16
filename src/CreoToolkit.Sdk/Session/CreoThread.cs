namespace CreoToolkit.Sdk.Session;

/// <summary>
/// 记录 Creo TOOLKIT 调用（及 native 释放）合法的唯一线程。Creo 非线程安全，所有释放必须在此线程执行。
/// 线程 id 由 <see cref="Capture()"/> 在初始化时捕获一次；<see cref="IsMainThread"/> 供释放路径判断当前线程是否为主线程。
/// <para>internal：线程门由 Attach 捕获、release/dispatcher 路径消费，不进 public 面。</para>
/// </summary>
internal static class CreoThread
{
    private static int _mainThreadId; // 0=尚未捕获；managed thread id 始终为正数

    /// <summary>将调用线程捕获为 Creo 主线程。初始化时调用一次。</summary>
    public static void Capture()
    {
        var current = Environment.CurrentManagedThreadId;
        var captured = Interlocked.CompareExchange(ref _mainThreadId, current, 0);
        if (captured != 0 && captured != current)
            throw new InvalidOperationException(
                "Creo 主线程已经绑定；不能从其它线程重新 Attach 或覆盖主线程标识。");
    }

    /// <summary>
    /// 显式设置主线程 id。供测试使用——测试线程需要指定（或故意错误指定）主线程。
    /// </summary>
    public static void Capture(int threadId) => Volatile.Write(ref _mainThreadId, threadId);

    /// <summary>清除已捕获的 id。用于测试用例间隔离。</summary>
    public static void Reset() => Volatile.Write(ref _mainThreadId, 0);

    /// <summary>已捕获的主线程 id；未捕获时为 <c>null</c>。</summary>
    public static int? MainThreadId
    {
        get
        {
            var captured = Volatile.Read(ref _mainThreadId);
            return captured == 0 ? null : captured;
        }
    }

    /// <summary>
    /// 当前线程是否为已捕获的 Creo 主线程。未捕获时返回 <c>false</c>（安全降级），
    /// 使未捕获状态下的调用被视为"非主线程"并将释放操作入队。
    /// </summary>
    public static bool IsMainThread
        => Volatile.Read(ref _mainThreadId) == Environment.CurrentManagedThreadId;
}
