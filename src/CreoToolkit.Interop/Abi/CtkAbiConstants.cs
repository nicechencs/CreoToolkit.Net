namespace CreoToolkit.Interop;

/// <summary>
/// L1 ABI 不变量的托管侧常量(与 <c>ctk_ops.h</c> / <c>ProSizeConst.h</c> / <c>ctk_error.h</c> 对齐)。
/// 真值在 <see cref="CtkAbi.Verify"/> 时由 <c>Ctk_GetAbiInfo</c> 回填并逐项断言, 防止托管镜像与 native 偷偷错位。
/// </summary>
public static class CtkAbiConstants
{
    /// <summary>PRO_NAME_SIZE: 参数名等定长宽串长度(wchar, 含 NUL)。</summary>
    public const int ProNameSize = 32;

    /// <summary>PRO_LINE_SIZE: ProLine 定长宽串长度(wchar, 含 NUL)。</summary>
    public const int ProLineSize = 81;

    // ---- 参数值类型 wire 值(对齐 CtkParamRecord.Kind; 与 C# CreoParamValueKind 非 Unset 部分一致)----
    /// <summary>CTK_PARAM_STRING。</summary>
    public const int ParamString = 1;
    /// <summary>CTK_PARAM_DOUBLE。</summary>
    public const int ParamDouble = 2;
    /// <summary>CTK_PARAM_INT。</summary>
    public const int ParamInt = 3;
    /// <summary>CTK_PARAM_BOOL。</summary>
    public const int ParamBool = 4;

    // ---- CtkError(导出返回 >0 时的 L1 内部错误码; <0 为透传 ProError)----
    /// <summary>CTK_OK。</summary>
    public const int Ok = 0;
    /// <summary>CTK_ERR_BAD_ARG。</summary>
    public const int ErrBadArg = 1;
    /// <summary>CTK_ERR_OUT_OF_MEM。</summary>
    public const int ErrOutOfMem = 2;
    /// <summary>CTK_ERR_INTERNAL。</summary>
    public const int ErrInternal = 3;
    /// <summary>CTK_ERR_CALLBACK。</summary>
    public const int ErrCallback = 4;
}
