namespace CreoToolkit.Sdk;

/// <summary>draft entity 数据 DATA 快照(对应 <c>ProDtlentityDataGet</c> → <c>ProDtlentitydata</c>)。
/// <para>暴露 5 字段(Entity/Color/FontName/LineWidth/Curve);
/// View 句柄留将来扩展。</para>
/// <para>L3 接缝在 bridge 立即 <c>ProDtlentitydataFree</c> + <c>ProCurvedataMemoryFree</c>
/// 释放,出口纯 DATA。</para>
/// <para><c>FontName</c>:对应 <c>wchar_t[32]</c> caller buffer,bridge 找 '\0' 截断;
/// 中文字体名 ≥ 31 UTF-16 code units 会被 Toolkit 固定缓冲截断,trace <c>font_truncated</c>。</para>
/// <para><c>Curve</c>:curve 几何 DATA 快照。bridge 经 <c>ProDtlentitydataCurveGet</c> 读入
/// caller-alloc <c>ptc_curve</c> union,按 <c>ProCurvedataTypeGet</c> 分派读取几何,
/// spline/bspline 内动态数组复制进托管后立即 <c>ProCurvedataMemoryFree</c>。
/// curve 读取失败时 <c>Curve</c> 为 null,不影响其余 3 字段可用。</para></summary>
public readonly record struct CreoDtlentityData(
    CreoDtlentity Entity,
    CreoColor Color,
    string FontName,
    double LineWidth,
    CreoDtlentityCurve? Curve);
