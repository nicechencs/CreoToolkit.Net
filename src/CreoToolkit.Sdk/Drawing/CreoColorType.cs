namespace CreoToolkit.Sdk;

/// <summary>命名色枚举(SDK 层 enum, public 面零 Pro* 名外露)。
/// <b>44 个 enum 成员</b>(<see cref="Undefined"/> + 42 命名色 + <see cref="Max"/>);
/// 值非连续(预留空位 7/11/13-17/26/27 等), 与 native 数值严格对齐以便 bridge 直接 cast。
/// <para>native 映射:对应 <c>ProColortype</c>, 真值来源
/// <c>Generated/Creo4_M140/Enums/ProToolkit.Enums.g.cs:19-66</c>。</para>
/// <para>自动校验测见 <c>CreoToolkit.Sdk.Tests/Drawing/CreoColorTests.cs</c> 的
/// <c>CreoColorType_value_alignment_with_ProColortype</c> fact (含反射 enum count 防 silent drift)。</para></summary>
public enum CreoColorType
{
    /// <summary>哨兵 = -1,未定义(对应 <c>PRO_COLOR_UNDEFINED</c>)。</summary>
    Undefined = -1,

    Letter = 0,
    Highlite = 1,
    Drawing = 2,
    Background = 3,
    HalfTone = 4,
    EdgeHighlight = 5,
    Dimmed = 6,
    Error = 8,
    Warning = 9,
    Sheetmetal = 10,
    Curve = 12,
    PreselHighlight = 18,
    Selected = 19,
    SecondarySelected = 20,
    PreviewGeom = 21,
    SecondaryPreview = 22,
    Datum = 23,
    Quilt = 24,
    Lww = 25,
    ShadedEdge = 28,
    SsHasFrozenFeat = 29,
    SsFrozenComponent = 30,
    SsFailedItems = 31,
    SsPackaged = 32,
    SsRecentSearch = 33,
    SsIntchGroupMembers = 34,
    SsReplacedComps = 35,
    SsFtInstances = 36,
    SsFtGenerics = 37,
    SsHasProprogram = 38,
    SsHasProprogramInput = 39,
    SsDrivenByProprogram = 40,
    SsModuleNodes = 41,
    SsCurrentDesignSol = 42,
    SsNoncurrentDesignSol = 43,
    SsRepresentativeDesignSol = 44,
    SsConfAsmNodes = 45,
    SsChildOfModifiedInDma = 46,
    ModchkHighlight = 47,
    ModchkParentHighlight = 48,
    SsUpdControlGroup = 49,
    SsContainsMathcadWorksheet = 50,

    /// <summary>哨兵 = 51,枚举上限(对应 <c>PRO_COLOR_MAX</c>);不应作为有效颜色返回。</summary>
    Max = 51,
}
