namespace CreoToolkit.Sdk;

/// <summary>draft entity curve 几何 DATA 快照。
/// <para><b>各 Kind → 字段映射契约</b>(native 真源 ProCurvedata.h,未列字段一律 null/0):</para>
/// <list type="table">
/// <item><term>Line / Arrow</term><description><c>End1</c>/<c>End2</c> 两端点; <c>NumPoints</c>=2</description></item>
/// <item><term>Arc</term><description><c>Origin</c>=圆心, <c>Vector1</c>/<c>Vector2</c>=平面单位向量,
/// <c>StartAngle</c>/<c>EndAngle</c>(弧度), <c>Radius</c></description></item>
/// <item><term>Circle</term><description><c>Center</c>=圆心, <c>Normal</c>=法向单位向量, <c>Radius</c>
/// (注意与 Arc/Ellipse 用 Origin 不同——Circle 用 Center/Normal,对应 ptc_circle 的
/// center/norm_axis_unit_vect 字段名)</description></item>
/// <item><term>Point</term><description><c>Origin</c>=位置; <c>NumPoints</c>=1</description></item>
/// <item><term>Ellipse</term><description><c>Origin</c>=中心, <c>Vector1</c>=长轴单位向量,
/// <c>Vector2</c>=法向单位向量, <c>StartAngle</c>/<c>EndAngle</c>,
/// <c>Radius</c>=长轴半径(major), <c>MinorRadius</c>=短轴半径(minor)</description></item>
/// <item><term>Text</term><description><c>Origin</c>=文字位置(ptc_text.location)</description></item>
/// <item><term>Spline</term><description><c>NumPoints</c>=插值点数(点/切向数组本体留 future 扩展)</description></item>
/// <item><term>BSpline</term><description><c>NumPoints</c>=knot 数, <c>NumControlPoints</c>=控制点数</description></item>
/// <item><term>Polygon / CompositeCurve / SurfaceCurve / ParamCurve</term>
/// <description>仅 <c>Kind</c>,几何字段全空(未实现读取)</description></item>
/// </list></summary>
/// <param name="MinorRadius">短轴半径,仅 Ellipse 使用(对应 ProEllipsedataGet 的 y_radius);
/// 其余 Kind 恒为 null。</param>
public readonly record struct CreoDtlentityCurve(
    CreoDtlentityCurveKind Kind,
    (double X, double Y, double Z)? End1,
    (double X, double Y, double Z)? End2,
    (double X, double Y, double Z)? Origin,
    (double X, double Y, double Z)? Vector1,
    (double X, double Y, double Z)? Vector2,
    double? StartAngle,
    double? EndAngle,
    double? Radius,
    (double X, double Y, double Z)? Center,
    (double X, double Y, double Z)? Normal,
    int NumPoints,
    int NumControlPoints,
    double? MinorRadius = null);

/// <summary>draft entity curve 类型,数值严格对应 native pro_ent_type。</summary>
public enum CreoDtlentityCurveKind
{
    None = -1,
    Point = 1,
    Line = 2,
    Arc = 3,
    Text = 6,
    Arrow = 8,
    Circle = 10,
    Spline = 12,
    BSpline = 19,
    Ellipse = 30,
    Polygon = 40,
    CompositeCurve = 41,
    SurfaceCurve = 56,
    ParamCurve = 62,
}
