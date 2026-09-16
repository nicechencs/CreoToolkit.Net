namespace CreoToolkit.Sdk;

/// <summary>尺寸公差类型,数值严格对应 native pro_dim_tol_types。</summary>
public enum CreoDimensionToleranceType
{
    Default = 1,
    PlusMinus = 2,
    Limits = 3,
    PlusMinusSymmetric = 4,
    SymmetricSuperscript = 8,
    Basic = 12,
}

/// <summary>尺寸公差表类型,数值严格对应 native ProToleranceTable。</summary>
public enum CreoToleranceTableType
{
    None = 0,
    General = 1,
    BrokenEdge = 2,
    Shafts = 3,
    Holes = 4,
}

/// <summary>尺寸配置,数值严格对应 native ProDimensionconfig。</summary>
public enum CreoDimensionConfig
{
    Leader = 0,
    Linear = 1,
    CenterLeader = 2,
    Angular = 3,
}

/// <summary>尺寸显示格式,数值严格对应 native ProDimensionDisplayFormat。</summary>
public enum CreoDimensionDisplayFormat
{
    Decimal = 0,
    Fractional = 1,
}

/// <summary>尺寸值显示方式,数值严格对应 native ProDimensionValueDisplay。</summary>
public enum CreoDimensionValueDisplay
{
    Nominal = 0,
    Override = 1,
    Hide = 2,
}

/// <summary>尺寸公差 label DATA 三元组。</summary>
public readonly record struct CreoDimensionToleranceLabel(
    CreoToleranceTableType TableType,
    string TableName,
    int Column);
