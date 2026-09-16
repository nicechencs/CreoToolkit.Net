using CreoToolkit.Interop.Generated;

namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// 参数与目标单位量纲不匹配时抛出。L3 守卫抛出时 <see cref="ErrorCode"/> 为 null;
/// native 返回 PRO_TK_INVALID_TYPE 时映射为 <see cref="ProError.InvalidType"/>。
/// </summary>
public sealed class UnitTypeMismatchException : CreoToolkitException
{
    /// <summary>发生量纲冲突的参数名。</summary>
    public string ParameterName { get; }

    /// <summary>参数当前期望的单位量纲。</summary>
    public ProUnitType ExpectedType { get; }

    /// <summary>调用方提供的单位量纲。</summary>
    public ProUnitType ProvidedType { get; }

    /// <summary>native 错误码;L3 守卫提前发现时为 null。</summary>
    public ProError? ErrorCode { get; }

    public UnitTypeMismatchException(
        string parameterName,
        ProUnitType expectedType,
        ProUnitType providedType)
        : this(parameterName, expectedType, providedType, null, null)
    {
    }

    public UnitTypeMismatchException(
        string parameterName,
        ProUnitType expectedType,
        ProUnitType providedType,
        ProError errorCode)
        : this(parameterName, expectedType, providedType, errorCode, null)
    {
    }

    public UnitTypeMismatchException(
        string parameterName,
        ProUnitType expectedType,
        ProUnitType providedType,
        string message)
        : this(parameterName, expectedType, providedType, null, message)
    {
    }

    private UnitTypeMismatchException(
        string parameterName,
        ProUnitType expectedType,
        ProUnitType providedType,
        ProError? errorCode,
        string? message)
        : base(message ?? BuildMessage(parameterName, expectedType, providedType, errorCode))
    {
        ParameterName = parameterName;
        ExpectedType = expectedType;
        ProvidedType = providedType;
        ErrorCode = errorCode;
    }

    private static string BuildMessage(
        string parameterName,
        ProUnitType expectedType,
        ProUnitType providedType,
        ProError? errorCode)
    {
        var suffix = errorCode is null ? string.Empty : $" Native error: {errorCode}.";
        return $"参数 '{parameterName}' 的单位量纲不匹配: expected={expectedType}, provided={providedType}.{suffix}";
    }
}