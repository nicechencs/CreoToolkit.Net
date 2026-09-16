namespace CreoToolkit.Sdk;

/// <summary>颜色 DATA 快照(对应 native <c>pro_color</c> union)。
/// <para>三态严格互斥:<see cref="CreoColorMethod.Default"/>(无 NamedType + RGB 全 0)/
/// <see cref="CreoColorMethod.Type"/>(NamedType non-null)/
/// <see cref="CreoColorMethod.Rgb"/>(R/G/B 0-1 有效)。
/// 必经 <see cref="Default"/> / <see cref="FromType"/> / <see cref="FromRgb"/> 3 个 factory 构造,
/// 私有 ctor enforce 二选一不变量。</para>
/// <para><b>SDK 不暴露 native</b>:<see cref="Method"/> 用 <see cref="CreoColorMethod"/>,
/// <see cref="NamedType"/> 用 <see cref="CreoColorType"/>。</para></summary>
public readonly struct CreoColor : IEquatable<CreoColor>
{
    /// <summary>颜色赋值方式(三态)。</summary>
    public CreoColorMethod Method { get; }

    /// <summary>命名色枚举值(仅 <see cref="Method"/>=<see cref="CreoColorMethod.Type"/> 时 non-null)。</summary>
    public CreoColorType? NamedType { get; }

    /// <summary>红分量 0-1(仅 <see cref="Method"/>=<see cref="CreoColorMethod.Rgb"/> 时有效)。</summary>
    public double Red { get; }

    /// <summary>绿分量 0-1(仅 <see cref="Method"/>=<see cref="CreoColorMethod.Rgb"/> 时有效)。</summary>
    public double Green { get; }

    /// <summary>蓝分量 0-1(仅 <see cref="Method"/>=<see cref="CreoColorMethod.Rgb"/> 时有效)。</summary>
    public double Blue { get; }

    private CreoColor(CreoColorMethod method, CreoColorType? namedType, double r, double g, double b)
    {
        Method = method;
        NamedType = namedType;
        Red = r;
        Green = g;
        Blue = b;
    }

    /// <summary>Default 三态:无 NamedType + RGB 全 0(对应 <c>PRO_COLOR_METHOD_DEFAULT</c>)。</summary>
    public static CreoColor Default { get; } = new(CreoColorMethod.Default, null, 0, 0, 0);

    /// <summary>Type 三态:NamedType non-null + RGB 全 0(对应 <c>PRO_COLOR_METHOD_TYPE</c>)。</summary>
    public static CreoColor FromType(CreoColorType namedType) =>
        new(CreoColorMethod.Type, namedType, 0, 0, 0);

    /// <summary>Rgb 三态:NamedType null + 三 double 0-1(对应 <c>PRO_COLOR_METHOD_RGB</c>);超界值不归一化(原样存)。</summary>
    public static CreoColor FromRgb(double red, double green, double blue) =>
        new(CreoColorMethod.Rgb, null, red, green, blue);

    public bool IsDefault => Method == CreoColorMethod.Default;
    public bool IsType => Method == CreoColorMethod.Type;
    public bool IsRgb => Method == CreoColorMethod.Rgb;

    public bool Equals(CreoColor other) =>
        Method == other.Method &&
        NamedType == other.NamedType &&
        Red == other.Red &&
        Green == other.Green &&
        Blue == other.Blue;

    public override bool Equals(object? obj) => obj is CreoColor o && Equals(o);

    public override int GetHashCode() => HashCode.Combine(Method, NamedType, Red, Green, Blue);

    public static bool operator ==(CreoColor a, CreoColor b) => a.Equals(b);
    public static bool operator !=(CreoColor a, CreoColor b) => !a.Equals(b);
}
