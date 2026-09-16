using CreoToolkit.Interop;
using CreoToolkit.Sdk.Session;

namespace CreoToolkit.Sdk.Lifetime;

/// <summary>
/// 注入 L2 <see cref="CreoReleaseGate"/> 的释放策略。在主线程释放时立即执行 native free；
/// 在其他线程（如 finalizer 线程）释放时将指针入队 <see cref="FinalizerReleaseQueue"/>，
/// 由主线程调用 <see cref="FinalizerReleaseQueue.Pump"/> 排干。
/// native free 动作通过注入提供，使本类可在无 L1/native 环境下单测（测试传计数委托）。
/// <para>
/// internal：释放管道基建，由 SDK 内部接线进 <see cref="CreoReleaseGate"/>；
/// 保证 SDK public 面无 native 边界泄漏，经 InternalsVisibleTo 对测试可见。
/// </para>
/// </summary>
internal sealed class CreoMainThreadReleaser : ICreoReleaser
{
    private readonly Action<nint, CtkFreeKind> _free;

    /// <summary>暂存非主线程释放请求的队列。</summary>
    public FinalizerReleaseQueue Queue { get; }

    /// <summary>
    /// 创建释放器：主线程释放时执行 <paramref name="free"/>，非主线程释放时入队 <paramref name="queue"/>。
    /// <paramref name="free"/> 按 <see cref="CtkFreeKind"/> 分派到对应 native free
    /// （选择副本→ProSelectionFree / elemtree→ProFeatureElemtreeFree / owned block→CreoToolkit_Free）。
    /// </summary>
    /// <param name="free">主线程合法的 native free 动作（按 kind 分派）。</param>
    /// <param name="queue">由主线程 pump 排干的共享队列。</param>
    public CreoMainThreadReleaser(Action<nint, CtkFreeKind> free, FinalizerReleaseQueue queue)
    {
        _free = free ?? throw new ArgumentNullException(nameof(free));
        Queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    /// <summary>
    /// 释放 <paramref name="handle"/>：主线程上立即执行 native free，
    /// 否则入队等待主线程 <see cref="FinalizerReleaseQueue.Pump"/> 排干。
    /// </summary>
    public void Release(nint handle, CtkFreeKind kind)
    {
        if (handle == IntPtr.Zero)
            return;

        if (CreoThread.IsMainThread)
            _free(handle, kind);          // 主线程：立即释放
        else
            Queue.Enqueue(handle, kind);  // 非主线程：入队等主线程排干
    }

    /// <summary>
    /// 排干非主线程入队的释放请求。转调 <see cref="FinalizerReleaseQueue.Pump"/>，须在主线程调用。
    /// </summary>
    /// <param name="maxItems">本次 pump 最多排干数(默认无上限,Dispose 路径用;未来业务路径节流时传上限)。</param>
    /// <returns>本次 pump 释放的句柄数。</returns>
    public int Pump(int maxItems = int.MaxValue) => Queue.Pump(_free, maxItems);
}
