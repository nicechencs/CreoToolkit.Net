namespace CreoToolkit.Interop;

/// <summary>
/// owned native 资源的释放种类，释放门据此分派对应 L1 free，互不混用（与 ctk_ops.h 对齐）：
/// <list type="bullet">
/// <item><see cref="OwnedBlock"/> → <c>CreoToolkit_Free</c>（CtkOwnedBlock 描述符）。</item>
/// <item><see cref="Elemtree"/> → <c>Ctk_ElemtreeFree</c>（LIVE 元素树，内部携 feature 上下文）。</item>
/// <item><see cref="Selection"/> → <c>ProSelectionFree</c>（BORROWED owned 选择副本，句柄即 ProSelection 指针）。</item>
/// </list>
/// DATA buffer 走 <c>Ctk_FreeBuffer</c>（copy-out 后即释，不形成长生命周期句柄），不在此枚举。
/// </summary>
public enum CtkFreeKind
{
    /// <summary>CtkOwnedBlock 描述符，经 <c>CreoToolkit_Free</c> 按 kind 内部分派释放。</summary>
    OwnedBlock = 0,

    /// <summary>LIVE 元素树不透明句柄，经 <c>Ctk_ElemtreeFree</c>（内部 <c>ProFeatureElemtreeFree</c> 携 feature 上下文）。</summary>
    Elemtree = 1,

    /// <summary>owned 选择副本（<c>ProSelectionCopy</c> 拷出），句柄即 ProSelection 裸指针，经 <c>ProSelectionFree</c> 释放。</summary>
    Selection = 2,
}
