namespace CreoToolkit.Sdk;

/// <summary>draft entity 颜色赋值方式(SDK 层 enum, public 面零 Pro* 名外露)。
/// 三态严格互斥, <see cref="CreoColor"/> 经 <see cref="CreoColor.Default"/> /
/// <see cref="CreoColor.FromType"/> / <see cref="CreoColor.FromRgb"/> 3 个 factory enforce。
/// <para>native 映射:对应 <c>pro_color_method</c>, 真值来源
/// <c>Generated/Creo4_M140/Enums/ProColor.Enums.g.cs:50-56</c>。</para></summary>
public enum CreoColorMethod
{
    /// <summary>使用 Pro/E 默认颜色(对应 <c>PRO_COLOR_METHOD_DEFAULT=0</c>;<see cref="CreoColor.NamedType"/> = null + RGB 全 0)。</summary>
    Default = 0,

    /// <summary>使用命名色 enum(对应 <c>PRO_COLOR_METHOD_TYPE=1</c>;<see cref="CreoColor.NamedType"/> non-null)。</summary>
    Type = 1,

    /// <summary>使用直接 RGB(对应 <c>PRO_COLOR_METHOD_RGB=2</c>;<see cref="CreoColor.Red"/>/<see cref="CreoColor.Green"/>/<see cref="CreoColor.Blue"/> 0-1 有效)。</summary>
    Rgb = 2,
}
