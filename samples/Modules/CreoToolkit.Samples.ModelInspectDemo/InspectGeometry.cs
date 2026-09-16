using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.ModelInspectDemo;

/// <summary>
/// 模型巡检 sample 内置的纯 .NET 几何工具集:不依赖 Creo native,仅基于 <see cref="CreoVec3"/> 值类型;
/// 可在 SDK 单元测试中独立验证。统一以参数化容差替代全局 EPSILON。
/// </summary>
public static class InspectGeometry
{
    /// <summary>判断三点 a / b / c 是否共线(向量 ab × ac 接近零向量)。</summary>
    public static bool ArePointsCollinear(CreoVec3 a, CreoVec3 b, CreoVec3 c, double tolerance = 1e-9)
    {
        var ab = b - a;
        var ac = c - a;
        return ab.IsParallelTo(ac, tolerance);
    }

    /// <summary>判断线段 ab 的方向向量是否与给定平面法向平行。</summary>
    public static bool IsSegmentNormalToPlane(CreoVec3 a, CreoVec3 b, CreoVec3 planeNormal,
        double tolerance = 1e-9)
    {
        var direction = b - a;
        return direction.IsParallelTo(planeNormal, tolerance);
    }

    /// <summary>三角形 abc 的面积(<c>0.5 · |ab × ac|</c>)。退化三角形返回 0。</summary>
    public static double TriangleArea(CreoVec3 a, CreoVec3 b, CreoVec3 c)
        => 0.5 * (b - a).Cross(c - a).Length();

    /// <summary>点 p 到经过 a 与 b 的直线的最短距离。
    /// <para>退化(a == b)时回退为点到点距离 <c>|p - a|</c>,避免 0 长度除法。</para></summary>
    public static double DistanceFromPointToLine(CreoVec3 p, CreoVec3 a, CreoVec3 b)
    {
        var ab = b - a;
        double baseLen = ab.Length();
        if (baseLen < 1e-12) return (p - a).Length();
        double area = TriangleArea(a, b, p);
        return 2.0 * area / baseLen;
    }

    /// <summary>从点集合中按容差移除指定点;不修改输入,返回新列表;
    /// 比较用 <see cref="CreoVec3.AreEqual"/>,不靠 record struct 的严格判等。</summary>
    public static IReadOnlyList<CreoVec3> RemovePoint(IReadOnlyList<CreoVec3> points, CreoVec3 target,
        double tolerance = 1e-8)
    {
        ThrowUtil.IfNull(points);
        var result = new List<CreoVec3>(points.Count);
        foreach (var pt in points)
        {
            if (CreoVec3.AreEqual(pt, target, tolerance)) continue;
            result.Add(pt);
        }
        return result;
    }
}
