namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// Creo Parametric TOOLKIT 状态码，取自 SDK 头文件 <c>ProToolkitErrors.h</c>
/// （Creo4 M140，<c>enum ProErrors</c>），以其为唯一真源。
/// 数值含义不可有损合并——<see cref="CreoResult"/> 始终保留原始 <see cref="ProError"/>。
/// </summary>
public enum ProError : int
{
    /// <summary>调用成功（PRO_TK_NO_ERROR）。</summary>
    NoError = 0,

    /// <summary>PRO_TK_GENERAL_ERROR。</summary>
    GeneralError = -1,

    /// <summary>PRO_TK_BAD_INPUTS。</summary>
    BadInputs = -2,

    /// <summary>PRO_TK_USER_ABORT — 用户中止交互操作，非错误。</summary>
    UserAbort = -3,

    /// <summary>PRO_TK_E_NOT_FOUND — 对象不存在；在查询类 API 是条件，在其他 API 可能是错误。</summary>
    NotFound = -4,

    /// <summary>PRO_TK_E_FOUND — 对象已找到，正向条件。</summary>
    Found = -5,

    /// <summary>PRO_TK_LINE_TOO_LONG。</summary>
    LineTooLong = -6,

    /// <summary>PRO_TK_CONTINUE — visit 回调中跳过/继续的信号，非错误。</summary>
    Continue = -7,

    /// <summary>PRO_TK_BAD_CONTEXT。</summary>
    BadContext = -8,

    /// <summary>PRO_TK_NOT_IMPLEMENTED。</summary>
    NotImplemented = -9,

    /// <summary>PRO_TK_OUT_OF_MEMORY。</summary>
    OutOfMemory = -10,

    /// <summary>PRO_TK_COMM_ERROR。</summary>
    CommError = -11,

    /// <summary>PRO_TK_NO_CHANGE。</summary>
    NoChange = -12,

    /// <summary>PRO_TK_SUPP_PARENTS。</summary>
    SuppParents = -13,

    /// <summary>PRO_TK_PICK_ABOVE — 用户选择了当前菜单上方条目（交互中止），非错误。</summary>
    PickAbove = -14,

    /// <summary>PRO_TK_INVALID_DIR。</summary>
    InvalidDir = -15,

    /// <summary>PRO_TK_INVALID_FILE。</summary>
    InvalidFile = -16,

    /// <summary>PRO_TK_CANT_WRITE。</summary>
    CantWrite = -17,

    /// <summary>PRO_TK_INVALID_TYPE。</summary>
    InvalidType = -18,

    /// <summary>PRO_TK_INVALID_PTR。</summary>
    InvalidPtr = -19,

    /// <summary>PRO_TK_UNAV_SEC。</summary>
    UnavSec = -20,

    /// <summary>PRO_TK_INVALID_MATRIX。</summary>
    InvalidMatrix = -21,

    /// <summary>PRO_TK_INVALID_NAME。</summary>
    InvalidName = -22,

    /// <summary>PRO_TK_NOT_EXIST。</summary>
    NotExist = -23,

    /// <summary>PRO_TK_CANT_OPEN。</summary>
    CantOpen = -24,

    /// <summary>PRO_TK_ABORT。</summary>
    Abort = -25,

    /// <summary>PRO_TK_NOT_VALID。</summary>
    NotValid = -26,

    /// <summary>PRO_TK_INVALID_ITEM。</summary>
    InvalidItem = -27,

    /// <summary>PRO_TK_E_AMBIGUOUS。</summary>
    Ambiguous = -36,

    /// <summary>PRO_TK_E_BUSY。</summary>
    Busy = -38,

    /// <summary>PRO_TK_E_IN_USE。</summary>
    InUse = -39,

    /// <summary>PRO_TK_NO_LICENSE。</summary>
    NoLicense = -40,

    /// <summary>PRO_TK_EMPTY。</summary>
    Empty = -45,

    /// <summary>PRO_TK_UNSUPPORTED。</summary>
    Unsupported = -58,

    /// <summary>PRO_TK_NO_PERMISSION。</summary>
    NoPermission = -59,

    /// <summary>PRO_TK_OUT_OF_RANGE。</summary>
    OutOfRange = -65,
}
