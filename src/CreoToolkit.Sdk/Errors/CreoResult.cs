namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// native 调用不抛异常时的分类结果：携带 <see cref="Outcome"/> 分类与原始 <see cref="Code"/>。
/// 调用方按 <see cref="Outcome"/> 分支，同时保留精确的 <see cref="ProError"/> 供诊断——
/// FOUND 与 NOT_FOUND 永不合并为同一桶。
/// </summary>
/// <param name="Code">native 返回的原始、无损状态码。</param>
/// <param name="Outcome">按函数语义对 <paramref name="Code"/> 的分类。</param>
public readonly record struct CreoResult(ProError Code, CreoOutcome Outcome)
{
    /// <summary>调用直接成功时为 true。</summary>
    public bool IsSuccess => Outcome == CreoOutcome.Success;

    /// <summary>查询条件成立（如 FOUND）时为 true。</summary>
    public bool IsConditionPositive => Outcome == CreoOutcome.ConditionPositive;

    /// <summary>查询条件不成立（如查询类 NOT_FOUND）时为 true。</summary>
    public bool IsConditionNegative => Outcome == CreoOutcome.ConditionNegative;

    /// <summary>用户中止操作（非错误）时为 true。</summary>
    public bool IsAborted => Outcome == CreoOutcome.Aborted;

    /// <summary>真实失败时为 true。</summary>
    public bool IsError => Outcome == CreoOutcome.Error;
}
