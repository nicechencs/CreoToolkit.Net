using System.Collections.Concurrent;
using CreoToolkit.Interop;

namespace CreoToolkit.Sdk.Lifetime;

/// <summary>
/// 线程安全的 native 句柄暂存队列，用于收集在 Creo 主线程以外触发的释放（包括 finalizer 驱动的释放）。
/// 这些释放不能就地执行，须入队后由 <see cref="Pump"/> 在主线程排干（如在消息/空闲循环中调用）。
/// free 动作通过注入提供，使本类无 L1/native 依赖，可完整单测。
/// <para>internal：释放管道基建，保证 SDK public 面无 <see cref="nint"/> 泄漏；经 InternalsVisibleTo 对测试可见。</para>
/// </summary>
internal sealed class FinalizerReleaseQueue
{
    private readonly ConcurrentQueue<(nint Handle, CtkFreeKind Kind)> _pending = new();

    /// <summary>当前等待释放的句柄数。</summary>
    public int Count => _pending.Count;

    /// <summary>将句柄（含 free kind）入队，等待主线程释放。任意线程调用安全。</summary>
    public void Enqueue(nint handle, CtkFreeKind kind)
    {
        if (handle != IntPtr.Zero)
            _pending.Enqueue((handle, kind));
    }

    /// <summary>
    /// 排干队列，对每个待释放句柄调用 <paramref name="release"/>（传入 <see cref="CtkFreeKind"/> 以分派正确的 native free）。
    /// 须在 Creo 主线程调用。pump 期间新入队的句柄留待下次排干，不自旋等待。
    /// </summary>
    /// <param name="release">主线程合法的 free 动作（按 kind 分派）。</param>
    /// <param name="maxItems">本次 pump 最多排干句柄数(默认 <see cref="int.MaxValue"/> 即不限);用于节流避免业务命令偶发停顿(pump 合约,业务路径节流挂在 <c>CreoSession.Run</c> 每次 native 调用后)。</param>
    /// <returns>本次 pump 释放的句柄数。</returns>
    public int Pump(Action<nint, CtkFreeKind> release, int maxItems = int.MaxValue)
    {
        ThrowUtil.IfNull(release);
        ThrowUtil.IfNegativeOrZero(maxItems);

        var snapshot = Math.Min(_pending.Count, maxItems); // 仅排干进入 pump 时已存在的句柄,且受 maxItems 上限
        var released = 0;

        while (released < snapshot && _pending.TryDequeue(out var item))
        {
            release(item.Handle, item.Kind);
            released++;
        }

        return released;
    }
}
