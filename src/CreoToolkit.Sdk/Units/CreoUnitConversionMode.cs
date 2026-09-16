namespace CreoToolkit.Sdk.Units;

/// <summary>主单位系统转换模式。
/// 对应 Pro Toolkit <c>ProUnitConvertType</c>，bridge 通过 switch 显式映射，public 面零 native 名外露。</summary>
public enum CreoUnitConversionMode
{
    /// <summary>保持尺寸数值，单位标签换(如 1mm → 1inch)。</summary>
    PreserveDimensionValues = 0,

    /// <summary>保持物理大小，数值按比例换(如 25.4mm → 1inch)。</summary>
    PreservePhysicalSize = 1,
}
