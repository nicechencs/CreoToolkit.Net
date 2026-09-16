namespace CreoToolkit.Interop;

/// <summary>
/// 进程级委托保活隔离集：Dispose 时 native 无法证明已停止回调的委托宿主
/// (Creo <c>ProNotificationUnset</c> 失败 / <c>ProCmdOptionAdd</c> 无反注册 API)
/// 一律 <see cref="Add"/> 到本集合，静态可达 → 委托对象不被 GC 回收 →
/// <see cref="System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate"/>
/// 产出的 thunk 稳定，Creo 若再次触发不会 use-after-free。
/// <para>
/// 语义与 <see cref="CreoToolkit.Sdk.Native.RetriableNativeSubscription"/> 的
/// <c>Quarantine</c> 一致：宁可有界泄漏到进程终止，不可 use-after-free。
/// 隔离对象一般是 Hub (含字典/列表强引用真实 native 委托)，只要 Hub 静态可达，
/// hub → 委托 → thunk 三层引用完整，registry 的 GCHandle 释放对已隔离对象无害。
/// </para>
/// </summary>
internal static class CallbackQuarantine
{
    private static readonly object Gate = new();
    private static readonly HashSet<object> Items = new();

    /// <summary>当前隔离项数量（测试观测用）。</summary>
    internal static int Count
    {
        get { lock (Gate) return Items.Count; }
    }

    /// <summary>加入隔离集；同一对象幂等。<paramref name="host"/> 不得为 null。</summary>
    internal static void Add(object host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        lock (Gate) Items.Add(host);
    }

    /// <summary>是否已隔离（测试观测用）。</summary>
    internal static bool Contains(object host)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        lock (Gate) return Items.Contains(host);
    }

    /// <summary>
    /// 清空隔离集。**仅供测试隔离用**（生产禁调用；一旦清空，native 若仍持函数指针即
    /// use-after-free）。测试之间用此方法保证互不污染。
    /// </summary>
    internal static void ClearForTests()
    {
        lock (Gate) Items.Clear();
    }
}
