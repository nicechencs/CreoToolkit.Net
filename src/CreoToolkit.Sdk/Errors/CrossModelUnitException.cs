namespace CreoToolkit.Sdk.Errors;

/// <summary>跨模型混用单位时抛出。该错误始终由 L3 守卫发现,没有 native 错误码路径。</summary>
public sealed class CrossModelUnitException : InvalidOperationException
{
    /// <summary>来源单位所属模型。</summary>
    public string SourceModel { get; }

    /// <summary>目标参数或目标单位所属模型。</summary>
    public string TargetModel { get; }

    /// <summary>构造跨模型单位异常。</summary>
    public CrossModelUnitException(string sourceModel, string targetModel)
        : this(sourceModel, targetModel, null)
    {
    }

    /// <summary>构造带自定义消息的跨模型单位异常。</summary>
    public CrossModelUnitException(string sourceModel, string targetModel, string? message)
        : base(message ?? $"单位不能跨模型使用: source='{sourceModel}', target='{targetModel}'。")
    {
        SourceModel = sourceModel;
        TargetModel = targetModel;
    }
}