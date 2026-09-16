namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// native 调用状态的四态分类，由 <see cref="ProErrorPolicy"/> 按 (api, code) 对计算。
/// 陷阱：同一数值码在不同 API 语义不同——例如 <c>E_NOT_FOUND</c> 在某些 API
/// 是硬错误，在 *Get/*Find 类查询中却是正常的"未命中"条件；因此不能单凭数值判定含义。
/// </summary>
public enum CreoOutcome
{
    /// <summary>调用成功（通常为 PRO_TK_NO_ERROR）。</summary>
    Success,

    /// <summary>查询条件成立（如对象 FOUND）。</summary>
    ConditionPositive,

    /// <summary>查询条件不成立（如 *Get/*Find 返回 NOT_FOUND）——非错误。</summary>
    ConditionNegative,

    /// <summary>用户中止交互操作（PICK_ABOVE / USER_ABORT）——非错误。</summary>
    Aborted,

    /// <summary>真实失败，应以 <see cref="CreoException"/> 向上抛出。</summary>
    Error,
}
