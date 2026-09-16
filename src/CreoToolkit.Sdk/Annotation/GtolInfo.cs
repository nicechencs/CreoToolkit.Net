namespace CreoToolkit.Sdk;

/// <summary>形位公差读回信息(纯 DTO)。</summary>
public readonly struct GtolInfo
{
    /// <summary>公差类型(原始 int,映射 ProGtolType)。</summary>
    public int RawType { get; }

    /// <summary>公差类型(强类型)。</summary>
    public CreoGtolType Type => (CreoGtolType)RawType;

    /// <summary>公差值字符串(如 "0.05")。</summary>
    public string? ValueString { get; }

    public GtolInfo(int rawType, string? valueString)
    {
        RawType = rawType;
        ValueString = valueString;
    }
}
