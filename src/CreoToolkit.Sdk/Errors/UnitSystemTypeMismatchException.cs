namespace CreoToolkit.Sdk.Errors;

/// <summary>单位系统类型不匹配时抛出(如 MLT 模型上设 FLT 系统)。SetPrincipalUnitSystem 守卫。
/// public 面只暴露字符串描述,避免 native <c>ProUnitsystemType</c> 外露。</summary>
public sealed class UnitSystemTypeMismatchException : CreoToolkitException
{
    /// <summary>当前模型的单位系统类型名(如 "MLT"/"FLT")。</summary>
    public string ExpectedType { get; }

    /// <summary>调用方提供的单位系统类型名。</summary>
    public string ProvidedType { get; }

    public UnitSystemTypeMismatchException(string expectedType, string providedType)
        : base($"单位系统类型不匹配: expected={expectedType}, provided={providedType}。")
    {
        ExpectedType = expectedType;
        ProvidedType = providedType;
    }
}
