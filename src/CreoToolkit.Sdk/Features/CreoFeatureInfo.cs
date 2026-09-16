namespace CreoToolkit.Sdk;

/// <summary>特征只读信息快照(DATA,不持 native 句柄)。</summary>
/// <param name="Id">特征 id(DHandle 持久 id)。</param>
/// <param name="FeatureType">特征类型编号(ProFeatType.h #define 宏,如 PRO_FEAT_COMPONENT=1000)。SDK 不包装为 enum,因为值域太大且 Creo 版本间不稳定。</param>
/// <param name="Status">特征状态。</param>
public readonly record struct CreoFeatureInfo(
    int Id,
    int FeatureType,
    CreoFeatureStatus Status);
