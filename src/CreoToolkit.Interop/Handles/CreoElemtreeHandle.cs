using System.Runtime.InteropServices;
using CreoToolkit.Interop.Generated;

namespace CreoToolkit.Interop;

/// <summary>
/// LIVE 元素树的 owned 句柄。持 unmanaged blob { feat, tree }，释放经
/// <see cref="CtkFreeKind.Elemtree"/> 走 DirectReleaser 的 ProFeatureElemtreeFree。
/// </summary>
public sealed class CreoElemtreeHandle : CreoSafeHandle
{
    /// <summary>创建无效的 owning 句柄(满足 <c>new()</c> acquire 约束)。</summary>
    public CreoElemtreeHandle() : base(owns: true) { }

    /// <summary>创建带显式所有权的无效句柄(borrowed 视图传 false)。</summary>
    public CreoElemtreeHandle(bool owns) : base(owns) { }

    /// <inheritdoc />
    protected override CtkFreeKind FreeKind => CtkFreeKind.Elemtree;

    /// <summary>抽取链接管 LIVE tree: 分配 blob 并写入释放所需上下文。</summary>
    internal void AssignFromExtractor(nint tree, in pro_model_item feat)
    {
        if (tree == IntPtr.Zero)
            return;

        var blob = Marshal.AllocHGlobal(Marshal.SizeOf<CtkElemtreeBlob>());
        Marshal.StructureToPtr(new CtkElemtreeBlob { Feat = feat, Tree = tree }, blob, fDeleteOld: false);
        Assign(blob);
    }
}

/// <summary>LIVE 元素树释放上下文 blob: ProFeatureElemtreeFree 需要 feat + tree。</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CtkElemtreeBlob
{
    public pro_model_item Feat;
    public IntPtr Tree;
}
