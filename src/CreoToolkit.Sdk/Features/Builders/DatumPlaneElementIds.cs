using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>datum plane 元素树 ID 常量(源于 ProElemId.h / ProFeatType.h / ProDtmPln.h 宏,不做 public enum)。</summary>
internal static class DatumPlaneElementIds
{
    internal const int PRO_E_DTMPLN_CONSTRAINTS = 410;        // ProElemId.h:463
    internal const int PRO_E_DTMPLN_CONSTRAINT = 411;         // ProElemId.h:464
    internal const int PRO_E_DTMPLN_CONSTR_TYPE = 412;        // ProElemId.h:465
    internal const int PRO_E_DTMPLN_CONSTR_REF = 413;         // ProElemId.h:466
    internal const int PRO_E_DTMPLN_CONSTR_REF_OFFSET = 414;  // ProElemId.h:467
    internal const int PRO_FEAT_DATUM = 923;                  // ProFeatType.h:21
    internal const int PRO_DTMPLN_OFFS = 3;                   // ProDtmPln.h pro_dtmpln_constr_type(THRU=0 起第 4 项)

    /// <summary>偏移距离小数位(对齐官方样例 UgDatumCreate.c 的 ProElementDecimalsSet(·,4))。</summary>
    internal const int OffsetDecimals = 4;
}

/// <summary>datum plane 子树组装(纯托管映射,可脱 Creo 单测)。</summary>
internal static class DatumPlaneTree
{
    /// <summary>
    /// offset 约束 datum plane 的元素树 spec(结构对照 ProDtmPln.h 头注树形):
    /// PRO_E_FEATURE_TREE → { PRO_E_FEATURE_TYPE=PRO_FEAT_DATUM,
    /// PRO_E_DTMPLN_CONSTRAINTS → PRO_E_DTMPLN_CONSTRAINT →
    /// { CONSTR_TYPE=PRO_DTMPLN_OFFS, CONSTR_REF=参照平面, CONSTR_REF_OFFSET=偏移值 } }。
    /// 参照须为平面(PRO_SURFACE plane / PRO_CSYS,见 ProDtmPln.h Note 1)。
    /// </summary>
    internal static ElemSpec BuildOffsetSpec(ItemRef referencePlane, double offset)
        => ElemSpec.Compound(FeatureElementIds.PRO_E_FEATURE_TREE,
            ElemSpec.Integer(FeatureElementIds.PRO_E_FEATURE_TYPE, DatumPlaneElementIds.PRO_FEAT_DATUM),
            ElemSpec.Compound(DatumPlaneElementIds.PRO_E_DTMPLN_CONSTRAINTS,
                ElemSpec.Compound(DatumPlaneElementIds.PRO_E_DTMPLN_CONSTRAINT,
                    ElemSpec.Integer(DatumPlaneElementIds.PRO_E_DTMPLN_CONSTR_TYPE, DatumPlaneElementIds.PRO_DTMPLN_OFFS),
                    ElemSpec.Reference(DatumPlaneElementIds.PRO_E_DTMPLN_CONSTR_REF, referencePlane),
                    ElemSpec.Double(DatumPlaneElementIds.PRO_E_DTMPLN_CONSTR_REF_OFFSET, offset,
                        decimals: DatumPlaneElementIds.OffsetDecimals))));
}
