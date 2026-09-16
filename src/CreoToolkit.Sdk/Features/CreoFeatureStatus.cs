namespace CreoToolkit.Sdk;

/// <summary>特征状态(映射 pro_feat_status)。</summary>
public enum CreoFeatureStatus
{
    Invalid = -1,
    Active = 0,
    Inactive = 1,
    FamtabSuppressed = 2,
    SimpRepSuppressed = 3,
    ProgramSuppressed = 4,
    Suppressed = 5,
    Unregenerated = 6,
}
