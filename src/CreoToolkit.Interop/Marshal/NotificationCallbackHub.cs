using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 把 <see cref="PopupNotifyHandler"/>（public delegate，接 .NET string）wrap 成 native typedef
/// <see cref="NativeMethods.ProPopupmenuNotifyFn"/>（接 char[32] 指针）。内部按 notifyType 维护一对一映射，
/// 便于 <see cref="Unregister"/> / <see cref="UnregisterAll"/> 精确释放。
/// <para>
/// **Dispose 顺序铁律**：调用方 <see cref="NativeCommandBridge"/> 在 Dispose 时必须先
/// <c>ProNotificationUnset</c>（Creo 端释放函数指针引用）再调本 hub 的 <see cref="UnregisterAll"/> 释放
/// GCHandle，否则 Creo 持悬空函数指针 → 段错误。任一 type 的 <c>ProNotificationUnset</c> 失败
/// 时，bridge 必须整体把 hub 加入 <see cref="CallbackQuarantine"/> 并放弃 Dispose——
/// 字典 <see cref="_byType"/> 对 native 委托的强引用是 thunk 保活的唯一根。
/// </para>
/// <para>
/// **注销顺序**：<see cref="RegisteredTypes"/> 按注册插入序返回，配合 bridge.Dispose 逆序循环，
/// 实现"按注册相反顺序注销"承诺（对齐 Host.Dispose 反序反注册铁律）。
/// </para>
/// </summary>
internal sealed class NotificationCallbackHub : IDisposable
{
    private readonly CallbackRegistry _registry;
    private readonly Dictionary<int, NativeMethods.ProPopupmenuNotifyFn> _byType = new();
    // 记录 type 的插入序：Commit 时新 key append；Unregister 时 Remove。
    private readonly List<int> _order = new();
    private readonly object _gate = new();
    private bool _disposed;

    public NotificationCallbackHub(CallbackRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>已注册的通知类型快照（按注册插入序），便于 UnregisterAll 不带锁迭代/逆序注销。</summary>
    public int[] RegisteredTypes
    {
        get { lock (_gate) return _order.ToArray(); }
    }

    public bool IsRegistered(int notifyType)
    {
        lock (_gate) return _byType.ContainsKey(notifyType);
    }

    /// <summary>
    /// 把 <paramref name="userHandler"/> wrap 并注册；返回函数指针给 <c>ProNotificationSet</c>。
    /// 同一 <paramref name="notifyType"/> 重复注册时 hub 保留覆盖能力（先 Unregister 旧的）——
    /// 仅供内部/测试直用；生产路径经 <c>NativeCommandBridge.NotificationSet</c> 走
    /// Prepare/Commit/Rollback 事务式替换，App 层 builder 在 native 注册前即拒绝重复声明。
    /// </summary>
    public nint Register(int notifyType, PopupNotifyHandler userHandler)
    {
        var nativeFn = Prepare(userHandler);
        Commit(notifyType, nativeFn);
        return Marshal.GetFunctionPointerForDelegate(nativeFn);
    }

    /// <summary>为事务式 native 注册创建并钉持候选 callback；尚不替换当前 type 映射。</summary>
    internal NativeMethods.ProPopupmenuNotifyFn Prepare(PopupNotifyHandler userHandler)
    {
        if (userHandler is null) throw new ArgumentNullException(nameof(userHandler));
        NativeMethods.ProPopupmenuNotifyFn nativeFn = menuNamePtr =>
        {
            try
            {
                string menuName = menuNamePtr == IntPtr.Zero
                    ? string.Empty
                    : Marshal.PtrToStringAnsi(menuNamePtr) ?? string.Empty;
                userHandler(menuName);
                return 0;
            }
            catch { return 1; }
        };
        lock (_gate)
        {
            ThrowIfDisposed();
            _registry.Register(nativeFn);
            return nativeFn;
        }
    }

    /// <summary>native Set 成功后提交候选项，此时才释放旧 callback 的 GC root。</summary>
    internal void Commit(int notifyType, NativeMethods.ProPopupmenuNotifyFn nativeFn)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_byType.TryGetValue(notifyType, out var existing))
            {
                _registry.Unregister(existing);
                // 覆盖不改插入序（同一 key 已在 _order，不 remove/add）。
            }
            else
            {
                _order.Add(notifyType);
            }
            _byType[notifyType] = nativeFn;
        }
    }

    /// <summary>native Set 失败时撤销候选 root；既有 type 映射保持不变。</summary>
    internal void Rollback(NativeMethods.ProPopupmenuNotifyFn nativeFn)
        => _registry.Unregister(nativeFn);

    /// <summary>释放 <paramref name="notifyType"/> 的 GCHandle；调用方应已先 ProNotificationUnset。</summary>
    public bool Unregister(int notifyType)
    {
        lock (_gate)
        {
            if (!_byType.TryGetValue(notifyType, out var fn)) return false;
            _registry.Unregister(fn);
            _byType.Remove(notifyType);
            _order.Remove(notifyType);
            return true;
        }
    }

    /// <summary>释放所有已注册通知的 GCHandle；调用方应已先对每个 type 调 ProNotificationUnset。</summary>
    public void UnregisterAll()
    {
        lock (_gate)
        {
            foreach (var fn in _byType.Values) _registry.Unregister(fn);
            _byType.Clear();
            _order.Clear();
        }
    }

    /// <summary>等价 <see cref="UnregisterAll"/>。Disposed 后 Register 抛 <see cref="ObjectDisposedException"/>。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var fn in _byType.Values) _registry.Unregister(fn);
            _byType.Clear();
            _order.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NotificationCallbackHub));
    }
}
