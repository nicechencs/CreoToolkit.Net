namespace CreoToolkit.Sdk;

/// <summary>模型类型(对齐 ProMdlfileType 全家族)。Unknown=未识别类型兜底,
/// 与 <see cref="CreoModelItemType.Unknown"/> 对偶,SDK 一致规则。
/// 枚举 int 值为 SDK 内部序号,与 native ProMdlType 值不同;映射逻辑在 CreoNativeBridge 双表内。
/// **禁止强转 int 传出/传给 native**——外部若要与 native ProMdlType 交互,一律走
/// CreoNativeBridge.ToProMdlType / FromProMdlType 双向映射,绕开会踩内部序号与 native 值不一致的雷。</summary>
public enum CreoModelType
{
    Unknown  = 0,
    Part     = 1,   // PRO_MDL_PART
    Assembly = 2,   // PRO_MDL_ASSEMBLY
    Drawing  = 3,   // PRO_MDL_DRAWING
    /// <summary>与 <see cref="Notebook"/> 同指 PTC .lay 类型(PRO_MDL_LAYOUT);保留成员仅为兼容,反向解析统一返回 Notebook。</summary>
    Layout   = 4,   // PRO_MDL_LAYOUT — 与 Notebook 同值,反向归 Notebook
    Format   = 5,   // PRO_MDL_DWGFORM
    Diagram  = 6,   // PRO_MDL_DIAGRAM
    Markup   = 7,   // PRO_MDL_MARKUP
    Notebook = 8,   // PRO_MDL_LAYOUT (.lay 文件即 Notebook,头注自证)
    [Obsolete("Creo 无 HARNESS 模型类型;制造 harness 是 ProMdlsubtype 子类型,不是 ProMdlType。保留成员仅为兼容既有消费点。")]
    Harness  = 11,  // 保留兼容;native 侧无对应 ProMdlType 成员
}

/// <summary>元素树节点的只读值摘要(DATA)。读出即与 native 脱钩, 不持任何句柄。</summary>
/// <param name="ElementId">元素 id(对应 ProElement 的 element id)。</param>
/// <param name="Level">在树中的层级(根=0)。</param>
public readonly record struct CreoElementNode(int ElementId, int Level);
