using G = CreoToolkit.Interop.Generated.NativeMethods;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Session;

namespace CreoToolkit.Sdk.Native;

/// <summary>单个 ProArray 句柄的所有权载体。三工厂对应释放策略:OwnedPlain(default
/// <c>ProArrayFree</c>)/ OwnedCustom(专用嵌套 free)/ Borrowed(no-op),外加
/// <see cref="Transfer"/> 转移态。Dispose 后句柄清零(内存铁律⑤:free 后置 0)、
/// 双 Dispose 幂等、Transfer 后 Dispose no-op。
/// <para>Transfer 语义:调用即转移所有权到下游 native(参照 ConstraintsSet setCalled 语序)——
/// 允许在 native 调用**之前**置位,rc 失败也不释放。构造中途异常且尚未 Transfer 时由 Dispose
/// 走对应策略释放。</para>
/// <para>BORROWED 严格 no-op:库 static / parent 结构 owned,任何 free 都会崩;
/// <see cref="Borrowed(IntPtr)"/> 只包装句柄以统一样板,不释放。</para>
/// <para>OwnedCustom 用于专用嵌套 free(如 <c>ProDimattachmentarrayFree</c>/
/// <c>ProSelectionarrayFree</c>/<c>ProWstringproarrayFree</c>),这些会递归释放元素;
/// 调用点显式绑定,不查 ownership.yml。</para>
/// <para>内部实现:三策略统一为单一 free 委托路径(OwnedPlain ≡ 预置 ProArrayFree 委托,
/// Borrowed ≡ null 委托),Dispose 状态机只有一条分支。</para></summary>
internal sealed class ProArrayScope : IDisposable
{
    /// <summary>OwnedPlain 的预置释放委托:default <c>ProArrayFree</c>(ref 语义经局部变量桥接)。
    /// free 失败不抛(Dispose 在 using/finally 里抛会吞掉主体原始异常且无重试机会),
    /// 只记录 rc 并隔离句柄(保守泄漏换稳定)。</summary>
    private static readonly Action<IntPtr> PlainArrayFree = static h =>
    {
        var local = h;
        var rc = G.ProArrayFree(ref local);
        if (rc != Interop.Generated.ProErrors.PRO_TK_NO_ERROR)
            CreoSdkLog.Warn("proarray", "free.failed",
                "ProArrayFree 失败，句柄不再重试释放（保守泄漏换稳定）",
                new { rc = (int)rc, handle = h.ToInt64() });
    };

    private IntPtr _handle;
    private readonly Action<IntPtr>? _free;   // null = Borrowed(禁 free)
    private readonly int _ownerThreadId;
    private bool _transferred;
    private bool _disposed;

    private ProArrayScope(IntPtr handle, Action<IntPtr>? free)
    {
        if (CreoThread.MainThreadId is int mainThreadId
            && mainThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException(
                $"ProArrayScope must be created on the captured Creo main thread {mainThreadId}; " +
                $"current thread is {Environment.CurrentManagedThreadId}.");
        _handle = handle;
        _free = free;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>句柄裸值。<see cref="Transfer"/> 之前用于喂 native 调用;
    /// 之后仍返原值(但 Dispose 不释放)。</summary>
    internal IntPtr Handle => _handle;

    /// <summary>是否已 Transfer(所有权已交出)。</summary>
    internal bool Transferred => _transferred;

    /// <summary>OwnedPlain 构造:Dispose 时经 <c>ProArrayFree</c>(default_free)释放。
    /// 适用于 mode=data + 无自定义嵌套 free 的常规读回路径。</summary>
    internal static ProArrayScope OwnedPlain(IntPtr handle)
        => new(handle, PlainArrayFree);

    /// <summary>OwnedCustom 构造:Dispose 时调注入的 <paramref name="customFree"/>。
    /// 用于专用嵌套 free(如 <c>ProDimattachmentarrayFree</c>/<c>ProSelectionarrayFree</c>/
    /// <c>ProWstringproarrayFree</c>——这些函数会递归释放数组元素,禁用默认 ProArrayFree
    /// 防遗漏元素释放)。</summary>
    internal static ProArrayScope OwnedCustom(IntPtr handle, Action<IntPtr> customFree)
    {
        ThrowUtil.IfNull(customFree);
        return new(handle, customFree);
    }

    /// <summary>Borrowed 构造:Dispose 为 no-op。用于库 static / parent 结构 owned 的
    /// borrowed 数组(如 <c>ProCableLocationsOnSegEndGet</c>/<c>ProEdgedataGet</c> 的
    /// uv_point_arr)——原指针谁都不许 free。</summary>
    internal static ProArrayScope Borrowed(IntPtr handle)
        => new(handle, null);

    /// <summary>解除释放责任。调用即转移所有权(参照 SetConstraints 的 setCalled 语序):
    /// 一经置位,即便本次 native 调用失败/后续路径抛异常,Dispose 也不 free。
    /// <para>惯用形态: <c>scope.Transfer(); var rc = G.ProXxxSet(..., scope.Handle);</c>——
    /// Set 调用即"承诺归 native",rc 失败也不 free。</para></summary>
    internal void Transfer() => _transferred = true;

    /// <summary>按策略释放句柄,同时清零(内存铁律⑤)。双 Dispose 幂等。
    /// Transfer 后为 no-op。BORROWED(free=null)永不 free。
    /// 清理失败只记录并隔离句柄，不从 Dispose 抛出，以免覆盖 using 主体的原始异常。</summary>
    public void Dispose()
    {
        if (_disposed) return;

        if (_transferred || _handle == IntPtr.Zero || _free is null)
        {
            _disposed = true;
            _handle = IntPtr.Zero;
            return;
        }

        var handle = _handle;
        _handle = IntPtr.Zero;
        _disposed = true;

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            // 跨线程 Dispose 不 free 也不抛：记录后保守泄漏（与 Cabling BORROWED 同一哲学）
            CreoSdkLog.Warn("proarray", "dispose.wrong-thread",
                "跨线程 Dispose：不释放，句柄已隔离（创建点守卫仍 fail-fast）",
                new { handle = handle.ToInt64(), ownerThreadId = _ownerThreadId,
                    currentThreadId = Environment.CurrentManagedThreadId });
            return;
        }

        try
        {
            _free(handle);
        }
        catch (Exception ex)
        {
            // 自定义 free 委托失败同样不抛，避免覆盖 using 主体的原始异常
            CreoSdkLog.Warn("proarray", "free.failed",
                "free 委托抛出，句柄不再重试释放",
                new { handle = handle.ToInt64(), exception = ex.GetType().FullName, ex.Message });
        }
    }
}
