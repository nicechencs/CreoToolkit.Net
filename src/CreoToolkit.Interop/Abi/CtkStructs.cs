using System.Runtime.InteropServices;

#pragma warning disable CS8618 // 互操作结构体由 marshaller 填充, 非空性约束不适用于其 string 字段

namespace CreoToolkit.Interop;

/// <summary>
/// DATA 值信封: CRT 分配的扁平数据块。<see cref="Data"/> 跨 ABI copy-out, 经 <c>Ctk_FreeBuffer</c> 在同一堆释放。
/// 镜像 <c>ctk_buffer.h CtkBuffer</c>(x64 = 16B)。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CtkBuffer
{
    /// <summary>CRT 拥有的数据块; <see cref="Count"/>==0 时为 NULL。</summary>
    public nint Data;
    /// <summary>元素个数。</summary>
    public int Count;
    /// <summary>单元素字节数。</summary>
    public int ElemSize;
}

/// <summary>
/// 参数定长 DATA 记录(copy-out 后无 native 所有权)。镜像 <c>ctk_ops.h CtkParamRecord</c>, sizeof=248。
/// <see cref="Kind"/> 决定哪个值字段有效; <see cref="Name"/>/<see cref="SVal"/> 是 Creo 定长宽串(含 NUL)。
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CtkParamRecord
{
    /// <summary>参数名(定长, 含 NUL); offset 0, 64B。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CtkAbiConstants.ProNameSize)]
    public string Name;

    /// <summary>CTK_PARAM_*; offset 64。</summary>
    public int Kind;

    /// <summary>Int; Bool 用 0/1; offset 68。</summary>
    public int IVal;

    /// <summary>Double; offset 72。</summary>
    public double DVal;

    /// <summary>String(ProLine, 定长含 NUL); offset 80。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CtkAbiConstants.ProLineSize)]
    public string SVal;

    /// <summary>ProParameterIsModified 结果; offset 244。</summary>
    public int Modified;
}

/// <summary>元素树节点定长 DATA 摘要。镜像 <c>ctk_ops.h CtkElemNode</c>(8B)。</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CtkElemNode
{
    /// <summary>元素 ID（来自 ProElementIdGet）。</summary>
    public int ElementId;
    /// <summary>遍历层级(根=0)。</summary>
    public int Level;
}

/// <summary>ProName 定长 DATA 记录。镜像 <c>ctk_ops.h CtkNameRecord</c>(64B)。</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct CtkNameRecord
{
    /// <summary>名称(ProName, 定长含 NUL)。</summary>
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CtkAbiConstants.ProNameSize)]
    public string Name;
}

/// <summary>
/// ABI 不变量自描述。镜像 <c>ctk_ops.h CtkAbiInfo</c>(字段顺序严格一致, 56B)。
/// 由 <c>Ctk_GetAbiInfo</c> 回填, <see cref="CtkAbi.Verify"/> 用以与托管镜像逐项断言。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CtkAbiInfo
{
    /// <summary>sizeof(CtkAbiInfo) 自校验。</summary>
    public int AbiInfoSize;
    /// <summary>sizeof(wchar_t)(Windows=2)。</summary>
    public int WcharSize;
    /// <summary>PRO_NAME_SIZE。</summary>
    public int ProNameSize;
    /// <summary>PRO_LINE_SIZE。</summary>
    public int ProLineSize;
    /// <summary>sizeof(CtkParamRecord)。</summary>
    public int ParamRecordSize;
    /// <summary>offsetof(CtkParamRecord, name)。</summary>
    public int OffName;
    /// <summary>offsetof(.., kind)。</summary>
    public int OffKind;
    /// <summary>offsetof(.., i_val)。</summary>
    public int OffIVal;
    /// <summary>offsetof(.., d_val)。</summary>
    public int OffDVal;
    /// <summary>offsetof(.., s_val)。</summary>
    public int OffSVal;
    /// <summary>offsetof(.., modified)。</summary>
    public int OffModified;
    /// <summary>sizeof(CtkElemNode)。</summary>
    public int ElemNodeSize;
    /// <summary>sizeof(CtkNameRecord)。</summary>
    public int NameRecordSize;
}
