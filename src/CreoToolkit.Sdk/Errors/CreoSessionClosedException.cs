namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// <c>CreoSession</c> 已 Dispose 后，调用其下任何 session-bound 对象
/// （<c>CreoModel</c>/<c>CreoParameters</c>/<c>CreoFeature</c> 等借用会话对象）时抛出。
/// ProMdl/ProFeature 等借用句柄在 session 关闭后立即失效，不允许内部 token 静默续用。
/// </summary>
public sealed class CreoSessionClosedException : InvalidOperationException
{
    /// <summary>构造异常，默认描述会话已关闭。</summary>
    public CreoSessionClosedException(string? message = null)
        : base(message ?? "CreoSession 已关闭, 其下借用会话对象不可再使用。") { }
}
