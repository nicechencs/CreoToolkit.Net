namespace CreoToolkit.Sdk;

/// <summary>
/// 模型项类型(数值对齐 Creo <c>ProType</c> / ProObjects.h, 保跨边界传递语义不变)。
/// <para>
/// bridge 从 native <c>ProModelitem.type</c>(int = ProType)解构时, 未识别的值会落到
/// <see cref="Unknown"/>,对应 <see cref="ICreoModelItem"/> 上层包成 <c>null</c>(强类型留白)。
/// </para>
/// <para>
/// <b>PRO_DATUM_PLANE = 176 刻意未列入本枚举</b>:Creo4 头文件不提供 <c>ProDtmpln*</c>
/// 查询函数族,官方 J-Link / OTK 也无 <c>pfcDatumPlane</c> geomitem 查询类;
/// 176 仅是 selection / reference filter 类型,不是可枚举的 modelitem 域。
/// datum plane 在会话里以两个身份并存:
/// (1) <c>PRO_FEATURE</c>(feat_type=<c>PRO_FEAT_DATUM</c>=923),经 <see cref="Feature"/> 覆盖;
/// (2) <c>PRO_SURFACE</c>(pro_srf_type=<c>PRO_SRF_PLANE</c>=34),经 <see cref="Surface"/> +
/// <c>CreoSurface.GetPlane()</c> 覆盖。
/// 复合视图(datum feature 上的平面 surface)走 <c>CreoSolid.ListDatumPlanes()</c> +
/// <c>CreoFeatures.ListDatumPlaneFeatures()</c>,不新增几何域。
/// </para>
/// </summary>
public enum CreoModelItemType
{
    /// <summary>占位/未识别 type(不支持的 modelitem 子类来自 native 时落此)。</summary>
    Unknown = 0,

    // ---- DHandle 衍生(持久 id, 属性化) ----

    /// <summary>PRO_FEATURE = 3。</summary>
    Feature = 3,

    /// <summary>PRO_DIMENSION = 8。</summary>
    Dimension = 8,

    /// <summary>PRO_REF_DIMENSION = 28。</summary>
    RefDimension = 28,

    /// <summary>PRO_SYMBOL_INSTANCE = 76；drawing detail symbol instance。</summary>
    SymbolInstance = 76,

    /// <summary>PRO_DRAFT_ENTITY = 77。draft entity 模型项。
    /// <para>真值来源 <c>Generated/Creo4_M140/Enums/ProObjects.Enums.g.cs:50</c>。
    /// (review #2 修: 原 spec/plan 误标 14, 实际 generated 是 77。)</para></summary>
    DraftEntity = 77,

    /// <summary>PRO_DRAFT_GROUP = 83。draft entities/symbols/notes 的分组容器。
    /// <para>真值来源 <c>ProObjects.h:59</c>;Sketch tab 里画的圆/圆弧/椭圆等 draft 几何常经此分组承载。</para></summary>
    DraftGroup = 83,

    /// <summary>PRO_NOTE = 68。</summary>
    Note = 68,

    /// <summary>PRO_DRAW_TABLE = 84。drawing 表格模型项。</summary>
    DrawingTable = 84,

    /// <summary>PRO_GTOL = 32。形位公差注释。</summary>
    Gtol = 32,

    /// <summary>PRO_LAYER = 117。</summary>
    Layer = 117,

    // ---- OHandle 衍生(id 经 ProXxxIdGet 取, 方法形态) ----

    /// <summary>PRO_SURFACE = 5。</summary>
    Surface = 5,

    /// <summary>PRO_EDGE = 6。</summary>
    Edge = 6,

    /// <summary>PRO_AXIS = 21。</summary>
    Axis = 21,

    /// <summary>PRO_CSYS = 25。</summary>
    Csys = 25,

    /// <summary>PRO_QUILT = 57。</summary>
    Quilt = 57,

    /// <summary>PRO_CURVE = 62。</summary>
    Curve = 62,

    /// <summary>PRO_POINT = 66。</summary>
    Point = 66,

    /// <summary>PRO_CABLE = 96。电缆 modelitem(Cabling 域)。</summary>
    Cable = 96,

    /// <summary>PRO_RELSET = 533；关系集 model item。</summary>
    RelationSet = 533,
}
