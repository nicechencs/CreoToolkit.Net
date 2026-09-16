namespace CreoToolkit.Sdk;

/// <summary>孔类型(值 = ProHole.h pro_hle_new_type 宏真值)。</summary>
public enum HoleType
{
    /// <summary>未识别(非法/读取失败)。</summary>
    Unknown = 0,

    /// <summary>PRO_HLE_NEW_TYPE_SKETCH = 6。</summary>
    Sketched = 6,

    /// <summary>PRO_HLE_CUSTOM_TYPE = 7。</summary>
    Custom = 7,

    /// <summary>PRO_HLE_NEW_TYPE_STRAIGHT = 16。</summary>
    Straight = 16,

    /// <summary>PRO_HLE_NEW_TYPE_STANDARD = 24。</summary>
    Standard = 24,
}

/// <summary>孔深度类型(值 = ProHole.h pro_hle_straight_dep_type 宏真值)。</summary>
public enum HoleDepthType
{
    /// <summary>未识别(非法/读取失败)。</summary>
    Unknown = 0,

    /// <summary>PRO_HLE_STRGHT_BLIND_DEPTH = 1。</summary>
    Blind = 1,

    /// <summary>PRO_HLE_STRGHT_THRU_NEXT_DEPTH = 2。</summary>
    ThruNext = 2,

    /// <summary>PRO_HLE_STRGHT_THRU_ALL_DEPTH = 3。</summary>
    ThruAll = 3,

    /// <summary>PRO_HLE_STRGHT_THRU_UNTIL_DEPTH = 4。</summary>
    ThruUntil = 4,

    /// <summary>PRO_HLE_STRGHT_UPTO_REF_DEPTH = 5。</summary>
    UptoRef = 5,

    /// <summary>PRO_HLE_STRGHT_NONE_DEPTH = 6。</summary>
    None = 6,

    /// <summary>PRO_HLE_STRGHT_SYM_DEPTH = 7。</summary>
    Symmetric = 7,
}

/// <summary>孔特征参数快照(纯 DATA)。从现有孔特征的深度元素树读取。</summary>
public readonly record struct HoleSpec(
    double Diameter,
    double Depth,
    HoleType Type,
    HoleDepthType DepthType);

/// <summary>HoleBuilder 当前配置的可构建性快照；可在调用 Build 前检查。</summary>
public readonly record struct HoleBuildCapability(bool IsSupported, string? UnsupportedReason)
{
    public void EnsureSupported()
    {
        if (!IsSupported)
            throw new NotSupportedException(UnsupportedReason);
    }
}

/// <summary>HoleBuilder 的显式能力矩阵。它只描述已经有 production bridge + Fake + 测试的组合。</summary>
public static class HoleBuilderCapabilities
{
    public static HoleBuildCapability Evaluate(HoleType type, HoleDepthType depthType)
    {
        if (type != HoleType.Straight)
            return new(false,
                type == HoleType.Standard
                    ? "Standard 孔请使用 StandardHoleBuilder（强类型 tapped/clearance）。"
                    : $"当前只支持 HoleType.Straight；{type} 尚无完整、可重定义的元素树实现。");
        if (depthType is not (HoleDepthType.Blind
            or HoleDepthType.ThruNext
            or HoleDepthType.ThruAll
            or HoleDepthType.ThruUntil
            or HoleDepthType.UptoRef))
            return new(false,
                $"Straight 孔暂不支持 {depthType}；请用 Blind/ThruNext/ThruAll/ThruUntil/UptoRef。");
        return new(true, null);
    }
}

/// <summary>孔元素树 ID 常量(逐一核对 ProElemId.h / ProHole.h / ProFeatForm.h 宏,不做 public enum)。
/// 树形结构见 ProHole.h 头注;straight+linear 蓝本:官方样例 UgHoleCreate.c 与
/// 内部 fixture 真机 dump。</summary>
internal static class HoleElementIds
{
    internal const int PRO_FEAT_HOLE = 911;                  // ProFeatType.h:9

    // ---- PRO_E_HLE_COM 子树 ----
    internal const int PRO_E_HLE_COM = 1671;                 // ProElemId.h:1721
    internal const int PRO_E_HLE_TYPE_NEW = 1672;            // ProElemId.h:1722
    internal const int PRO_E_HLE_STAN_TYPE = 1673;
    internal const int PRO_E_HLE_ADD_THREAD = 1674;
    internal const int PRO_E_HLE_ADD_CBORE = 1675;
    internal const int PRO_E_HLE_ADD_CSINK = 1676;
    internal const int PRO_E_DIAMETER = 82;                  // ProElemId.h:109(straight 孔直径)
    internal const int PRO_E_HLE_HOLEDIAM = 1347;            // ProElemId.h:1390(standard 孔直径)
    internal const int PRO_E_HLE_DRILLANGLE = 1348;
    internal const int PRO_E_HLE_CBOREDEPTH = 1349;
    internal const int PRO_E_HLE_CBOREDIAM = 1350;
    internal const int PRO_E_HLE_CSINKDIAM = 1351;
    internal const int PRO_E_HLE_CSINKANGLE = 1352;
    internal const int PRO_E_HLE_THRDDEPTH = 1354;
    internal const int PRO_E_HLE_THRDSERIS = 1356;
    internal const int PRO_E_HLE_SCREWSIZE = 1358;
    internal const int PRO_E_HLE_FITTYPE = 1661;
    internal const int PRO_E_HLE_DEPTH = 1662;
    internal const int PRO_E_HLE_DRILLDEPTH = 1663;
    internal const int PRO_E_HLE_THRD_DEPTH = 1680;
    internal const int PRO_E_HOLE_STD_DEPTH = 1684;          // ProElemId.h:1734
    internal const int PRO_E_HOLE_DEPTH_TO = 1686;           // ProElemId.h:1736
    internal const int PRO_E_HOLE_DEPTH_TO_TYPE = 1688;      // ProElemId.h:1738
    internal const int PRO_E_EXT_DEPTH_TO_VALUE = 434;       // ProElemId.h:489
    internal const int PRO_E_EXT_DEPTH_TO_REF = 435;
    internal const int PRO_E_HOLE_DEPTH_FROM = 1685;         // ProElemId.h:1735
    internal const int PRO_E_HOLE_DEPTH_FROM_TYPE = 1687;    // ProElemId.h:1737
    internal const int PRO_E_HLE_CRDIR_FLIP = 1689;
    internal const int PRO_E_HLE_ADD_EXIT_CSINK = 3110;
    internal const int PRO_E_STD_HOLE_DEPTH_REF = 3237;
    internal const int PRO_E_HLE_DEPTH_DIM_TYPE = 3741;
    internal const int PRO_E_HLE_MAKE_LIGHTWT = 3887;
    internal const int PRO_E_HLE_TOP_CLEARANCE = 6053;

    // ---- PRO_E_HLE_PLACEMENT 子树 ----
    internal const int PRO_E_HLE_PLACEMENT = 1340;           // ProElemId.h:1383
    internal const int PRO_E_HLE_PRIM_REF = 1341;            // ProElemId.h:1384
    internal const int PRO_E_HLE_PL_TYPE = 696;              // ProElemId.h:798
    internal const int PRO_E_HLE_DIM_REF1 = 704;             // ProElemId.h:806
    internal const int PRO_E_HLE_DIM_DIST1 = 706;            // ProElemId.h:808
    internal const int PRO_E_HLE_DIM_REF2 = 707;             // ProElemId.h:809
    internal const int PRO_E_HLE_DIM_DIST2 = 708;            // ProElemId.h:810

    // ---- 枚举宏值(ProHole.h / ProFeatForm.h) ----
    internal const int PRO_HLE_TYPE_STRAIGHT_FORM = 1;       // ProHole.h:748 PRO_HLE_TYPE_STRAIGHT = PRO_EXTRUDE(ProFeatForm.h:18)
    internal const int PRO_HLE_PL_TYPE_LIN = 1;              // ProHole.h:897
    internal const int PRO_HLE_STRGHT_NONE_DEPTH = 6;        // ProHole.h:817
    internal const int PRO_HLE_REGULAR = -1;                 // ProHole.h:805 pro_hle_light_wt_flag
    internal const int PRO_HOLE_GEN_CLRNCE = -1;             // ProHole.h:869 pro_hle_top_clrnc_flag(官方名)
}
