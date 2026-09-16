namespace CreoToolkit.Sdk;

/// <summary>
/// 3D 平面值类型(点 + 法向, 不可变, 值语义)。
/// <para>
/// 与 Pro/Toolkit <c>Ptc_plane</c>(e1/e2/e3/origin)互转:<see cref="Origin"/>=origin,<see cref="Normal"/>=e3。
/// e1/e2(面内基)本类型不存(法向已唯一确定平面)。
/// </para>
/// <para>
/// <b>Normal 是否单位长由工厂决定</b>:<see cref="FromOriginNormal"/> 强制归一(用户入口);
/// <see cref="FromThreePoints"/> 不归一(保留 cross 原长,免一次 sqrt+除)。
/// 实例算子 <see cref="SignedDistanceTo"/> 内部除以 |Normal|,两条工厂出来的实例语义都对。
/// </para>
/// <para>
/// <b>判等警告</b>:<c>record struct</c> 的 <c>==</c> 是严格逐位浮点比较,业务请用 <see cref="AreEqual"/>(字段级 1e-12)。
/// <c>AreEqual</c> 不做几何同一性归一(Normal 反向 / Origin 取面内不同点都判 false),
/// 几何同一性请用 <see cref="IsCoincidentWith"/>。
/// </para>
/// </summary>
public readonly record struct CreoPlane(CreoVec3 Origin, CreoVec3 Normal)
{
    // ---- 静态工厂 ----

    /// <summary>点 + 法向构造,强制归一 Normal。Normal 零向量(|N|&lt;1e-12)抛 <see cref="InvalidOperationException"/>。</summary>
    public static CreoPlane FromOriginNormal(CreoVec3 origin, CreoVec3 normal)
    {
        if (normal.Length() < 1e-12)
            throw new InvalidOperationException("Cannot construct plane: normal is zero vector.");
        return new CreoPlane(origin, normal.Normalize());
    }

    /// <summary>三共面点构造,Normal=(p2-p1)×(p3-p1)(<b>不</b>归一,保留原长)。
    /// 三点共线(|cross|&lt;1e-12)抛 <see cref="InvalidOperationException"/>。</summary>
    public static CreoPlane FromThreePoints(CreoVec3 p1, CreoVec3 p2, CreoVec3 p3)
    {
        var n = (p2 - p1).Cross(p3 - p1);
        if (n.Length() < 1e-12)
            throw new InvalidOperationException("Cannot construct plane: three points are collinear.");
        return new CreoPlane(p1, n);
    }

    /// <summary>从 Pro/Toolkit <c>Ptc_plane</c> 的 origin + e3(法向)构造。
    /// Origin=origin, Normal=e3(<b>不</b>归一,保留 Pro 端原值;调用方需要单位长经 <see cref="WithNormalizedNormal"/>)。
    /// 数组长度 ≠ 3 抛 <see cref="ArgumentException"/>(经 <see cref="CreoVec3.FromArray"/> 校验)。</summary>
    public static CreoPlane FromPtcPlane(double[] origin, double[] e3)
        => new(CreoVec3.FromArray(origin), CreoVec3.FromArray(e3));

    /// <summary>字段级容差判等(逐分量 Origin + Normal 比较)。<b>不</b>做几何同一性归一。</summary>
    public static bool AreEqual(CreoPlane a, CreoPlane b, double tolerance = 1e-12)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return CreoVec3.AreEqual(a.Origin, b.Origin, tolerance)
            && CreoVec3.AreEqual(a.Normal, b.Normal, tolerance);
    }

    // ---- 实例操作 ----

    /// <summary>带符号距离(沿 Normal 正方向为正):<c>((point - Origin) · Normal) / |Normal|</c>。
    /// Normal 零向量抛 <see cref="InvalidOperationException"/>。</summary>
    public double SignedDistanceTo(CreoVec3 point)
    {
        double nLen = Normal.Length();
        if (nLen < 1e-12)
            throw new InvalidOperationException("Plane normal is zero vector; distance undefined.");
        return (point - Origin).Dot(Normal) / nLen;
    }

    /// <summary>无符号距离 = <c>|SignedDistanceTo(point)|</c>。Normal 零向量抛。</summary>
    public double DistanceTo(CreoVec3 point) => Math.Abs(SignedDistanceTo(point));

    /// <summary>两平面间距(仅平行时定义)。
    /// <para>平行 → 任取 <paramref name="other"/>.Origin 距本面的带符号距离(平行面间距恒定);
    /// 非平行 → 返 null。Normal 零向量抛。</para></summary>
    public double? DistanceTo(CreoPlane other, double tolerance = 1e-9)
    {
        if (!Normal.IsParallelTo(other.Normal, tolerance)) return null;
        return SignedDistanceTo(other.Origin);
    }

    /// <summary>点是否在平面上(<c>|SignedDistanceTo(p)| ≤ tolerance</c>)。</summary>
    public bool Contains(CreoVec3 point, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return Math.Abs(SignedDistanceTo(point)) <= tolerance;
    }

    /// <summary>点到平面的垂足(投影点)= <c>point - SignedDistanceTo(point) · (Normal/|Normal|)</c>。
    /// Normal 零向量抛。</summary>
    public CreoVec3 ClosestPointTo(CreoVec3 point)
    {
        double nLen = Normal.Length();
        if (nLen < 1e-12)
            throw new InvalidOperationException("Plane normal is zero vector; projection undefined.");
        double signed = (point - Origin).Dot(Normal) / nLen;
        var nUnit = Normal * (1.0 / nLen);
        return point - signed * nUnit;
    }

    /// <summary>几何同一平面判定:Normal 平行 + <paramref name="other"/>.Origin 落在 this 上。
    /// 允许 Normal 反向、Origin 取面内不同点。</summary>
    public bool IsCoincidentWith(CreoPlane other, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        if (!Normal.IsParallelTo(other.Normal, tolerance)) return false;
        return Contains(other.Origin, tolerance);
    }

    /// <summary>返 Normal 单位化后的副本(Origin 不动)。Normal 零向量抛。
    /// 用于需要单位长 Normal 的外部 API / 诊断输出。</summary>
    public CreoPlane WithNormalizedNormal()
    {
        if (Normal.Length() < 1e-12)
            throw new InvalidOperationException("Cannot normalize: normal is zero vector.");
        return this with { Normal = Normal.Normalize() };
    }
}
