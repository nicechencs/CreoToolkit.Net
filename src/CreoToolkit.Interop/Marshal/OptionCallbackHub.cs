namespace CreoToolkit.Interop;

/// <summary>
/// 把 <see cref="OptionCmdActHandler"/> / <see cref="OptionCmdValHandler"/>（public delegate）wrap 成
/// native typedef <see cref="NativeMethods.uiCmdCmdActFn"/> / <see cref="NativeMethods.uiCmdCmdValFn"/>，
/// 经共享 <see cref="CallbackRegistry"/> GCHandle 钉持，返回稳定函数指针给 ProCmdOptionAdd。
/// <para>
/// **不写 L1 C wrapper**（不踩 L1 准入门）：option callback 在边界处简单函数指针 + 托管异常 try/catch
/// 兜在 wrap 层（native AV/损坏状态仍会进程崩，但签名 ABI 错位是首要风险，靠类型对齐 +
/// ABI 测试 + smoke 跑出来）。
/// </para>
/// <para>
/// **生命周期与 Dispose 顺序**：本 hub 不直接持 GCHandle（委托寿命由 registry 管），
/// 但保留对包装后委托对象的强引用，确保 registry 不被外部提前回收时 hub 仍能在 Dispose 时清理。
/// Creo 端 <c>ProCmdOptionAdd</c> 无反注册 API：只要曾经成功注册，命令终身持函数指针，
/// bridge.Dispose 时必须把 hub 整体隔离到 <see cref="CallbackQuarantine"/>（禁 Dispose）
/// —— 隔离对象经字典/列表 → 委托对象 → thunk 保持可达。
/// </para>
/// </summary>
internal sealed class OptionCallbackHub : IDisposable
{
    private readonly CallbackRegistry _registry;
    // 保留强引用：每个 wrap 出来的 native delegate 在这里持一份；hub.Dispose 时 Unregister。
    private readonly List<NativeMethods.uiCmdCmdActFn> _actDelegates = new();
    private readonly List<NativeMethods.uiCmdCmdValFn> _valDelegates = new();
    private readonly object _gate = new();
    private bool _disposed;

    public OptionCallbackHub(CallbackRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>当前持有的 (actFn, valFn) 对数。</summary>
    public int Count
    {
        get { lock (_gate) return _actDelegates.Count; }
    }

    /// <summary>是否已有 wrap 出的 option 注册项（供 bridge.Dispose 判定是否需隔离）。</summary>
    public bool HasRegistrations => Count > 0;

    /// <summary>Wrap 一次注册的凭证：精确回滚同一 (act, val) 对时用。</summary>
    internal sealed class Registration
    {
        internal Registration(NativeMethods.uiCmdCmdActFn act, NativeMethods.uiCmdCmdValFn val)
        {
            Act = act;
            Val = val;
        }
        internal NativeMethods.uiCmdCmdActFn Act { get; }
        internal NativeMethods.uiCmdCmdValFn Val { get; }
    }

    /// <summary>
    /// 把 <paramref name="userAct"/> / <paramref name="userVal"/> wrap 成 native typedef 兼容的委托，
    /// 注册到 <see cref="CallbackRegistry"/>，返回 (actFnPtr, valFnPtr) 与凭证给调用方。
    /// 若后续 <c>ProCmdOptionAdd</c> 失败，调用方须 <see cref="Rollback"/>(handle) 撤销本次登记，
    /// 避免失败项永久滞留造成 hub 隔离阈值虚高。
    /// </summary>
    public (nint actFnPtr, nint valFnPtr, Registration handle) Wrap(
        OptionCmdActHandler userAct, OptionCmdValHandler userVal)
    {
        if (userAct is null) throw new ArgumentNullException(nameof(userAct));
        if (userVal is null) throw new ArgumentNullException(nameof(userVal));

        // wrap：托管异常 try/catch 兜在边界；appData 是 typedef 第 3 参（头文件文档"Not used"）。
        NativeMethods.uiCmdCmdActFn nativeAct = (cmdId, pValue, appData) =>
        {
            try { userAct(cmdId, pValue); return 0; }
            catch { return 1; }
        };
        NativeMethods.uiCmdCmdValFn nativeVal = (cmdId, pValue) =>
        {
            try { userVal(cmdId, pValue); return 0; }
            catch { return 1; }
        };

        lock (_gate)
        {
            ThrowIfDisposed();
            _actDelegates.Add(nativeAct);
            _valDelegates.Add(nativeVal);
            nint actPtr = _registry.Register(nativeAct);
            nint valPtr = _registry.Register(nativeVal);
            return (actPtr, valPtr, new Registration(nativeAct, nativeVal));
        }
    }

    /// <summary>
    /// 精确撤销一次 <see cref="Wrap"/>：从内部强引用列表摘除同一对象引用，并从 registry
    /// 释放对应 GCHandle。仅在 <c>ProCmdOptionAdd</c> 失败等未把函数指针递交给 native 的情况调用。
    /// </summary>
    public void Rollback(Registration handle)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        lock (_gate)
        {
            // Disposed 后回滚无意义（列表已清空）；静默返回，避免 dispose 竞争抛异常。
            if (_disposed) return;
            // 用 object 引用比较（List<T>.Remove 用 EqualityComparer<T>.Default，
            // 委托类型 == 走 MulticastDelegate.Equals，同实例返 true，这里精确摘除本次注册的对象引用）。
            _actDelegates.Remove(handle.Act);
            _valDelegates.Remove(handle.Val);
            _registry.Unregister(handle.Act);
            _registry.Unregister(handle.Val);
        }
    }

    /// <summary>释放本 hub 持有的所有 native delegate 在 registry 中的 GCHandle。可重复调用。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var act in _actDelegates) _registry.Unregister(act);
            foreach (var val in _valDelegates) _registry.Unregister(val);
            _actDelegates.Clear();
            _valDelegates.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OptionCallbackHub));
    }
}
