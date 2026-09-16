using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 在 native 可能回调期间持有托管委托的 GC root（keep-alive），防止 GC 在 native 还持有函数指针时
/// 提前回收委托。每个注册的委托用 <see cref="GCHandle.Alloc"/>(normal) 建 GC root，以委托实例为 key
/// 存入字典；<see cref="Unregister"/> 释放对应 GCHandle。
/// </summary>
/// <remarks>
/// <para>线程安全：所有修改均由内部锁保护。</para>
/// <para>
/// ⚠ <b>命名澄清</b>:用的是 normal GCHandle(不是 pinned),
/// 委托对象**可能被 GC 移动**,但因有 GC root 不会被回收。<see cref="Marshal.GetFunctionPointerForDelegate"/>
/// 返回的是一个 native trampoline 函数指针,这个指针**在委托存活期间稳定**(不随 GC 移动),所以
/// native 持有这个指针调用是安全的。"Pin" 一词不精确(historical legacy 名),实际语义=GC root + stable native fn pointer。
/// </para>
/// </remarks>
public sealed class CallbackRegistry : IDisposable
{
    private readonly Dictionary<object, GCHandle> _alive = new();
    private readonly object _gate = new();
    private bool _disposed;

    /// <summary>当前持有的委托数量。</summary>
    public int Count
    {
        get { lock (_gate) return _alive.Count; }
    }

    /// <summary>指定委托是否已注册。</summary>
    public bool IsRegistered(Delegate callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        lock (_gate) return _alive.ContainsKey(callback);
    }

    /// <summary>
    /// 注册 <paramref name="callback"/>，建立 GC 根，返回在 <see cref="Unregister"/>（或 <see cref="Dispose"/>）前持续有效的 native 函数指针。
    /// 重复注册同一委托是幂等的，返回稳定指针。
    /// </summary>
    public nint Register(Delegate callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_alive.ContainsKey(callback))
                _alive[callback] = GCHandle.Alloc(callback); // Normal handle：建立 GC 根
            return Marshal.GetFunctionPointerForDelegate(callback);
        }
    }

    /// <summary>
    /// 释放 <paramref name="callback"/> 的 GCHandle 根。返回后委托可被 GC 回收，函数指针不得再使用。
    /// </summary>
    /// <returns>已注册并成功释放返回 true；未注册返回 false。</returns>
    public bool Unregister(Delegate callback)
    {
        if (callback is null) throw new ArgumentNullException(nameof(callback));
        lock (_gate)
        {
            if (!_alive.TryGetValue(callback, out var gch))
                return false;
            _alive.Remove(callback);
            if (gch.IsAllocated) gch.Free();
            return true;
        }
    }

    /// <summary>释放所有 GCHandle 根。可重复调用。</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var gch in _alive.Values)
                if (gch.IsAllocated) gch.Free();
            _alive.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CallbackRegistry));
    }
}
