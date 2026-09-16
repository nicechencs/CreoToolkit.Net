namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// <see cref="Check.Eval"/> 将 native 调用分类为 <see cref="CreoOutcome.Error"/> 时抛出。
/// 携带失败的 <see cref="ErrorCode"/>、来源 <see cref="ApiName"/> 及 native 侧附加的 <see cref="NativeMessage"/>。
/// 非错误结果（成功/条件/中止）不抛，以 <see cref="CreoResult"/> 返回。
/// </summary>
public sealed class CreoException : CreoToolkitException
{
    /// <summary>导致失败的原始 native 状态码。</summary>
    public ProError ErrorCode { get; }

    /// <summary>返回失败码的 Creo TOOLKIT 函数名。</summary>
    public string ApiName { get; }

    /// <summary>native 侧附加的错误详情；无则为 null。</summary>
    public string? NativeMessage { get; }

    /// <summary>构造失败 native 调用的异常。</summary>
    public CreoException(string apiName, ProError errorCode, string? nativeMessage = null)
        : base(BuildMessage(apiName, errorCode, nativeMessage))
    {
        ApiName = apiName;
        ErrorCode = errorCode;
        NativeMessage = nativeMessage;
    }

    private static string BuildMessage(string apiName, ProError errorCode, string? nativeMessage)
    {
        var baseText = $"{apiName} failed with {errorCode} ({(int)errorCode}).";
        return string.IsNullOrEmpty(nativeMessage) ? baseText : $"{baseText} {nativeMessage}";
    }
}
