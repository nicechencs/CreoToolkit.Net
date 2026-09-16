using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>spec 树节点值种类(仅覆盖写入链当前支持的形态,按需扩)。</summary>
internal enum ElemSpecValueKind
{
    /// <summary>无值(compound/array 容器节点)。</summary>
    None = 0,

    /// <summary>整数值(含枚举,如 PRO_E_FEATURE_TYPE)。</summary>
    Integer = 1,

    /// <summary>双精度值(可带小数位,ProElementDecimalsSet 先于 DoubleSet)。</summary>
    Double = 2,

    /// <summary>参照值(ItemRef → Bridge 内临时 ProSelection→ProReference,树接管)。</summary>
    Reference = 3,
}

/// <summary>
/// 特征元素树 spec 节点(纯托管 DTO,不持任何 native 句柄)。
/// Bridge 在一次 native 调用序列内按此树 Alloc→Set→Add→Create→Free,
/// 不向上层暴露半成品树(内存五不变量:恰好一次/主线程门/free 后清零)。
/// </summary>
internal sealed class ElemSpec
{
    private ElemSpec(int elementId, ElemSpecValueKind kind,
        int intValue, double doubleValue, int? decimals, ItemRef? reference, ElemSpec[] children)
    {
        ElementId = elementId;
        ValueKind = kind;
        IntValue = intValue;
        DoubleValue = doubleValue;
        Decimals = decimals;
        ItemReference = reference;
        Children = children;
    }

    /// <summary>元素 id(PRO_E_* 常量,ProElemId.h)。</summary>
    public int ElementId { get; }

    /// <summary>值种类。</summary>
    public ElemSpecValueKind ValueKind { get; }

    /// <summary>整数值(<see cref="ElemSpecValueKind.Integer"/> 时有效)。</summary>
    public int IntValue { get; }

    /// <summary>双精度值(<see cref="ElemSpecValueKind.Double"/> 时有效)。</summary>
    public double DoubleValue { get; }

    /// <summary>小数位(非空时 ProElementDecimalsSet 先于 DoubleSet,顺序约束)。</summary>
    public int? Decimals { get; }

    /// <summary>参照项(<see cref="ElemSpecValueKind.Reference"/> 时有效)。</summary>
    public ItemRef? ItemReference { get; }

    /// <summary>子节点(compound/array 语义;值节点为空)。</summary>
    public IReadOnlyList<ElemSpec> Children { get; }

    /// <summary>容器节点(compound/array)。</summary>
    public static ElemSpec Compound(int elementId, params ElemSpec[] children)
        => new(elementId, ElemSpecValueKind.None, 0, 0, null, null, children);

    /// <summary>整数值节点。</summary>
    public static ElemSpec Integer(int elementId, int value)
        => new(elementId, ElemSpecValueKind.Integer, value, 0, null, null, Array.Empty<ElemSpec>());

    /// <summary>双精度值节点(decimals 非空时先设小数位)。</summary>
    public static ElemSpec Double(int elementId, double value, int? decimals = null)
        => new(elementId, ElemSpecValueKind.Double, 0, value, decimals, null, Array.Empty<ElemSpec>());

    /// <summary>参照值节点。</summary>
    public static ElemSpec Reference(int elementId, ItemRef reference)
        => new(elementId, ElemSpecValueKind.Reference, 0, 0, null, reference, Array.Empty<ElemSpec>());
}
