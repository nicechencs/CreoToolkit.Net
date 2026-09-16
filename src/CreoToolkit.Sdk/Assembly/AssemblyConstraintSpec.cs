using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>装配约束类型(值 = ProAsmcomp.h pro_asm_constraint_type 宏真值)。
/// 排除 PRO_ASM_AUTO(14) 与 PRO_ASM_EXPLICIT(35),头注 internal use。</summary>
public enum CreoAssemblyConstraintType
{
    /// <summary>PRO_ASM_MATE = 0。</summary>
    Mate = 0,

    /// <summary>PRO_ASM_MATE_OFF = 1。</summary>
    MateOffset = 1,

    /// <summary>PRO_ASM_ALIGN = 2。</summary>
    Align = 2,

    /// <summary>PRO_ASM_ALIGN_OFF = 3。</summary>
    AlignOffset = 3,

    /// <summary>PRO_ASM_INSERT = 4。</summary>
    Insert = 4,

    /// <summary>PRO_ASM_ORIENT = 5。</summary>
    Orient = 5,

    /// <summary>PRO_ASM_CSYS = 6。</summary>
    Csys = 6,

    /// <summary>PRO_ASM_TANGENT = 7。</summary>
    Tangent = 7,

    /// <summary>PRO_ASM_PNT_ON_SRF = 8。</summary>
    PointOnSurface = 8,

    /// <summary>PRO_ASM_EDGE_ON_SRF = 9。</summary>
    EdgeOnSurface = 9,

    /// <summary>PRO_ASM_DEF_PLACEMENT = 10。</summary>
    DefaultPlacement = 10,

    /// <summary>PRO_ASM_SUBSTITUTE = 11。</summary>
    Substitute = 11,

    /// <summary>PRO_ASM_PNT_ON_LINE = 12。</summary>
    PointOnLine = 12,

    /// <summary>PRO_ASM_FIX = 13。</summary>
    Fix = 13,

    // 14 = PRO_ASM_AUTO (internal use, 排除)

    /// <summary>PRO_ASM_ALIGN_ANG_OFF = 15。</summary>
    AlignAngOffset = 15,

    /// <summary>PRO_ASM_MATE_ANG_OFF = 16。</summary>
    MateAngOffset = 16,

    /// <summary>PRO_ASM_CSYS_PNT = 17。</summary>
    CsysPoint = 17,

    /// <summary>PRO_ASM_LINE_NORMAL = 18。</summary>
    LineNormal = 18,

    /// <summary>PRO_ASM_LINE_COPLANAR = 19。</summary>
    LineCoplanar = 19,

    /// <summary>PRO_ASM_LINE_PARL = 20。</summary>
    LineParallel = 20,

    /// <summary>PRO_ASM_LINE_DIST = 21。</summary>
    LineDistance = 21,

    /// <summary>PRO_ASM_PNT_DIST = 22。</summary>
    PointDistance = 22,

    /// <summary>PRO_ASM_INSERT_NORM = 23。</summary>
    InsertNormal = 23,

    /// <summary>PRO_ASM_INSERT_PARL = 24。</summary>
    InsertParallel = 24,

    /// <summary>PRO_ASM_PNT_ON_LINE_DIST = 25。</summary>
    PointOnLineDistance = 25,

    /// <summary>PRO_ASM_PNT_ON_SRF_DIST = 26。</summary>
    PointOnSurfaceDistance = 26,

    /// <summary>PRO_ASM_EDGE_ON_SRF_DIST = 27。</summary>
    EdgeOnSurfaceDistance = 27,

    /// <summary>PRO_ASM_EDGE_ON_SRF_ANG = 28。</summary>
    EdgeOnSurfaceAngle = 28,

    /// <summary>PRO_ASM_EDGE_ON_SRF_NORMAL = 29。</summary>
    EdgeOnSurfaceNormal = 29,

    /// <summary>PRO_ASM_ALIGN_NODEP_ANGLE = 30。</summary>
    AlignNodependentAngle = 30,

    /// <summary>PRO_ASM_MATE_NODEP_ANGLE = 31。</summary>
    MateNodependentAngle = 31,

    /// <summary>PRO_ASM_LINE_ANGLE = 32。</summary>
    LineAngle = 32,

    /// <summary>PRO_ASM_EDGE_ON_SRF_PARL = 33。</summary>
    EdgeOnSurfaceParallel = 33,

    /// <summary>PRO_ASM_SRF_NORMAL = 34。</summary>
    SurfaceNormal = 34,

    // 35 = PRO_ASM_EXPLICIT (internal use, 排除)
}

/// <summary>约束参照侧向(值 = ProAsmcomp.h pro_datum_side 宏真值)。</summary>
public enum CreoAssemblyConstraintSide
{
    /// <summary>PRO_DATUM_SIDE_RED = -1。</summary>
    Red = -1,

    /// <summary>PRO_DATUM_SIDE_NONE = 0。</summary>
    None = 0,

    /// <summary>PRO_DATUM_SIDE_YELLOW = 1。</summary>
    Yellow = 1,
}

/// <summary>装配约束 spec(纯托管 DTO,Bridge 单次调用序列内消费)。
/// 构造时从 CreoModelItem 提取内部 ItemRef。</summary>
public readonly struct AssemblyConstraintSpec
{
    /// <summary>约束类型。</summary>
    public CreoAssemblyConstraintType Type { get; }

    /// <summary>装配侧参照(内部 token)。</summary>
    internal ItemRef AsmRef { get; }

    /// <summary>组件侧参照(内部 token)。</summary>
    internal ItemRef CompRef { get; }

    /// <summary>偏移值,仅 MateOffset/AlignOffset 有意义。</summary>
    public double? Offset { get; }

    /// <summary>装配侧侧向(默认 Yellow)。</summary>
    public CreoAssemblyConstraintSide AsmSide { get; }

    /// <summary>组件侧侧向(默认 Yellow)。</summary>
    public CreoAssemblyConstraintSide CompSide { get; }

    /// <summary>装配侧参照所在组件的特征 id(件对件形态:参照位于装配内某组件模型中);
    /// null = 参照为装配自身 item(既有形态)。</summary>
    public int? AsmRefCompFeatId { get; }

    /// <summary>构造约束 spec(侧向默认 Yellow)。</summary>
    public AssemblyConstraintSpec(
        CreoAssemblyConstraintType type,
        CreoModelItem asmRef,
        CreoModelItem compRef,
        double? offset = null,
        int? asmRefCompFeatId = null)
        : this(type, asmRef, compRef, CreoAssemblyConstraintSide.Yellow, CreoAssemblyConstraintSide.Yellow,
              offset, asmRefCompFeatId)
    {
    }

    /// <summary>构造约束 spec(含侧向)。</summary>
    public AssemblyConstraintSpec(
        CreoAssemblyConstraintType type,
        CreoModelItem asmRef,
        CreoModelItem compRef,
        CreoAssemblyConstraintSide asmSide,
        CreoAssemblyConstraintSide compSide,
        double? offset = null,
        int? asmRefCompFeatId = null)
    {
        ThrowUtil.IfNull(asmRef);
        ThrowUtil.IfNull(compRef);

        if (offset.HasValue
            && type != CreoAssemblyConstraintType.MateOffset
            && type != CreoAssemblyConstraintType.AlignOffset)
        {
            throw new ArgumentException(
                $"Offset 仅 MateOffset/AlignOffset 允许非空,当前类型 {type}。", nameof(offset));
        }

        Type = type;
        AsmRef = asmRef.Ref;
        CompRef = compRef.Ref;
        Offset = offset;
        AsmSide = asmSide;
        CompSide = compSide;
        AsmRefCompFeatId = asmRefCompFeatId;
    }
}

/// <summary>约束读回 DTO(单条约束的快照)。
/// RawType 保留 native 原始值(含 AUTO=14/EXPLICIT=35 等枚举未定义值);
/// Type 便捷属性直接 cast,无名值以 int 保留(C# 枚举允许任意 int)。</summary>
public readonly struct AssemblyConstraintInfo
{
    /// <summary>约束类型原始 int 值(pro_asm_constraint_type)。</summary>
    public int RawType { get; }

    /// <summary>约束类型(枚举 cast,未定义值以 int 保留)。</summary>
    public CreoAssemblyConstraintType Type => (CreoAssemblyConstraintType)RawType;

    /// <summary>偏移值; OffsetGet 失败/无意义时 null。</summary>
    public double? Offset { get; }

    /// <summary>装配侧参照项类型; 读不到时 null。</summary>
    public CreoModelItemType? AsmRefType { get; }

    /// <summary>装配侧参照项 id; 读不到时 null。</summary>
    public int? AsmRefId { get; }

    /// <summary>组件侧参照项类型; 读不到时 null。</summary>
    public CreoModelItemType? CompRefType { get; }

    /// <summary>组件侧参照项 id; 读不到时 null。</summary>
    public int? CompRefId { get; }

    internal AssemblyConstraintInfo(
        int rawType, double? offset,
        CreoModelItemType? asmRefType, int? asmRefId,
        CreoModelItemType? compRefType, int? compRefId)
    {
        RawType = rawType;
        Offset = offset;
        AsmRefType = asmRefType;
        AsmRefId = asmRefId;
        CompRefType = compRefType;
        CompRefId = compRefId;
    }
}
