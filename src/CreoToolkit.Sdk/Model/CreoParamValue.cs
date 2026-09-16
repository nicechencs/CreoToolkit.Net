namespace CreoToolkit.Sdk;

/// <summary>参数值的类型标签。对应 Creo PRO_PARAM_VALUE 的四种标量。</summary>
public enum CreoParamValueKind
{
    /// <summary>
    /// 未初始化哨兵(=0, 即 <c>default(CreoParamValue)</c> 的 Kind)。永不由 <c>OfXxx</c> 工厂方法产生；
    /// 任何 <c>AsXxx()</c> 对 Unset 值均抛 <see cref="InvalidOperationException"/>，杜绝默认值被当成合法 String。
    /// </summary>
    Unset = 0,
    /// <summary>字符串(wstring copy-out)。</summary>
    String,
    /// <summary>双精度。</summary>
    Double,
    /// <summary>整数。</summary>
    Int,
    /// <summary>布尔。</summary>
    Bool,
}

/// <summary>
/// 参数值(DATA 三态)。copy-out-free-in 后的纯值快照——无任何 native 所有权, 因此是
/// <c>readonly struct</c> 且非 <see cref="IDisposable"/>。用显式 <see cref="Kind"/> + 类型化访问器
/// (<see cref="AsString"/>/<see cref="AsDouble"/>/<see cref="AsInt"/>/<see cref="AsBool"/>)表达联合,
/// **不暴露 object 值**——避免装箱与类型约束丢失。访问器与实际 Kind 不符时抛
/// <see cref="InvalidOperationException"/>, 让误用在编码期即暴露。
/// </summary>
public readonly struct CreoParamValue : IEquatable<CreoParamValue>
{
    private readonly string? _str;   // String
    private readonly double _num;    // Double
    private readonly long _int;      // Int
    private readonly bool _bool;     // Bool

    /// <summary>该值的类型标签。</summary>
    public CreoParamValueKind Kind { get; }

    private CreoParamValue(CreoParamValueKind kind, string? str, double num, long i, bool b)
    {
        Kind = kind;
        _str = str;
        _num = num;
        _int = i;
        _bool = b;
    }

    /// <summary>构造字符串值。</summary>
    public static CreoParamValue OfString(string value)
        => new(CreoParamValueKind.String, value ?? throw new ArgumentNullException(nameof(value)), 0, 0, false);

    /// <summary>构造双精度值。</summary>
    public static CreoParamValue OfDouble(double value)
        => new(CreoParamValueKind.Double, null, value, 0, false);

    /// <summary>构造整数值。</summary>
    public static CreoParamValue OfInt(int value)
        => new(CreoParamValueKind.Int, null, 0, value, false);

    /// <summary>构造布尔值。</summary>
    public static CreoParamValue OfBool(bool value)
        => new(CreoParamValueKind.Bool, null, 0, 0, value);

    /// <summary>取字符串值; Kind 非 String 时抛 <see cref="InvalidOperationException"/>。</summary>
    public string AsString() => Kind == CreoParamValueKind.String
        ? _str!
        : throw Mismatch(CreoParamValueKind.String);

    /// <summary>取双精度值; Kind 非 Double 时抛 <see cref="InvalidOperationException"/>。</summary>
    public double AsDouble() => Kind == CreoParamValueKind.Double
        ? _num
        : throw Mismatch(CreoParamValueKind.Double);

    /// <summary>取整数值; Kind 非 Int 时抛 <see cref="InvalidOperationException"/>。</summary>
    public int AsInt() => Kind == CreoParamValueKind.Int
        ? checked((int)_int)
        : throw Mismatch(CreoParamValueKind.Int);

    /// <summary>取布尔值; Kind 非 Bool 时抛 <see cref="InvalidOperationException"/>。</summary>
    public bool AsBool() => Kind == CreoParamValueKind.Bool
        ? _bool
        : throw Mismatch(CreoParamValueKind.Bool);

    private InvalidOperationException Mismatch(CreoParamValueKind requested)
        => new($"CreoParamValue 的 Kind 是 {Kind}, 不能按 {requested} 读取。");

    /// <inheritdoc />
    public bool Equals(CreoParamValue other)
        => Kind == other.Kind && Kind switch
        {
            CreoParamValueKind.String => _str == other._str,
            CreoParamValueKind.Double => _num.Equals(other._num),
            CreoParamValueKind.Int => _int == other._int,
            CreoParamValueKind.Bool => _bool == other._bool,
            _ => false,
        };

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CreoParamValue v && Equals(v);

    /// <inheritdoc />
    public override int GetHashCode() => Kind switch
    {
        CreoParamValueKind.String => HashCode.Combine(Kind, _str),
        CreoParamValueKind.Double => HashCode.Combine(Kind, _num),
        CreoParamValueKind.Int => HashCode.Combine(Kind, _int),
        CreoParamValueKind.Bool => HashCode.Combine(Kind, _bool),
        _ => Kind.GetHashCode(),
    };

    /// <summary>人类可读形式(诊断用), 形如 <c>Double(3.14)</c>。</summary>
    public override string ToString() => Kind switch
    {
        CreoParamValueKind.String => $"String({_str})",
        CreoParamValueKind.Double => $"Double({_num})",
        CreoParamValueKind.Int => $"Int({_int})",
        CreoParamValueKind.Bool => $"Bool({_bool})",
        _ => "Unknown",
    };

    public static bool operator ==(CreoParamValue a, CreoParamValue b) => a.Equals(b);
    public static bool operator !=(CreoParamValue a, CreoParamValue b) => !a.Equals(b);
}
