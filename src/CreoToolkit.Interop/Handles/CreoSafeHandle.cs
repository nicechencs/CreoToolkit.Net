using System.Diagnostics;
using System.Runtime.InteropServices;
using CreoToolkit.Interop.Diagnostics;

namespace CreoToolkit.Interop;

/// <summary>
/// 所有 toolkit native 句柄的基类。管控 owned/borrowed 所有权契约，释放统一经
/// <see cref="CreoReleaseGate"/> 分派；释放成功后将底层指针归零，防止二次释放与悬空访问。
/// </summary>
public abstract class CreoSafeHandle : SafeHandle
{
    private readonly bool _owns;

    /// <param name="owns">
    /// true 表示本对象负责释放（LIVE / owned 副本）；
    /// false 表示 BORROWED 视图，生命周期由外部管理，释放为空操作。
    /// </param>
    protected CreoSafeHandle(bool owns) : base(IntPtr.Zero, ownsHandle: true)
    {
        _owns = owns;
    }

    /// <summary>是否持有所有权；BORROWED 视图为 false，释放被抑制。</summary>
    public bool Owns => _owns;

    /// <summary>
    /// 本句柄释放走哪类 L1 free(默认 <see cref="CtkFreeKind.OwnedBlock"/> = <c>CreoToolkit_Free</c>)。
    /// 需专用 free 的句柄(如 <see cref="CreoElemtreeHandle"/>)覆写此属性。
    /// </summary>
    protected virtual CtkFreeKind FreeKind => CtkFreeKind.OwnedBlock;

    /// <inheritdoc />
    public override bool IsInvalid => handle == IntPtr.Zero;

    /// <summary>
    /// 设置本句柄包裹的 native 指针。仅子类(如 <see cref="CreoElemtreeHandle"/>) 在受控
    /// 接管路径可调用,防止外部代码任意接管指针所有权。
    /// </summary>
    internal void Assign(nint h)
    {
        Debug.Assert(handle == IntPtr.Zero, "重复 Assign 将泄漏旧指针并破坏计数");
        SetHandle(h);
        // owned 且非零才计数——与 ReleaseHandle 的释放条件对称
        if (_owns && h != IntPtr.Zero)
            CtkResourceGate.RecordAlloc(FreeKind);
    }

    /// <inheritdoc />
    protected sealed override bool ReleaseHandle()
    {
        if (!_owns || handle == IntPtr.Zero)
            return true; // BORROWED 或已归零：无需释放

        var kind = FreeKind;
        CreoReleaseGate.Release(handle, kind); // 默认直接 P/Invoke；L3 层可替换为主线程编排
        CtkResourceGate.RecordFree(kind);
        handle = IntPtr.Zero;              // 释放后归零，防止悬空访问
        return true;
    }
}
