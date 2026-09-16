namespace CreoToolkit.Interop;

/// <summary>
/// BORROWED 三态的 owned 选择副本句柄。持 <c>ProSelectionCopy</c> 拷出的 ProSelection 裸指针
/// （无需 blob，指针即句柄），释放经 <see cref="CtkFreeKind.Selection"/> 走 DirectReleaser 的 ProSelectionFree。
/// <para>与 <see cref="CreoElemtreeHandle"/> 同纪律：Dispose 为确定性主路径，SafeHandle 自带的
/// critical finalizer 为兜底网——漏 Dispose 时 finalizer 线程触发释放，经
/// <see cref="CreoReleaseGate"/> 入队后由主线程 pump，绝不在 finalizer 线程直调 ProSelectionFree。</para>
/// </summary>
public sealed class CreoSelectionHandle : CreoSafeHandle
{
    /// <summary>创建无效的 owning 句柄（满足 <c>new()</c> acquire 约束）。</summary>
    public CreoSelectionHandle() : base(owns: true) { }

    /// <summary>创建带显式所有权的无效句柄（borrowed 视图传 false，释放被抑制）。</summary>
    public CreoSelectionHandle(bool owns) : base(owns) { }

    /// <inheritdoc />
    protected override CtkFreeKind FreeKind => CtkFreeKind.Selection;

    /// <summary>接管一份 owned ProSelection 副本指针；Zero 表示空副本，句柄保持无效、释放为空操作。</summary>
    internal void AssignCopy(nint selection)
    {
        if (selection != IntPtr.Zero)
            Assign(selection);
    }
}
