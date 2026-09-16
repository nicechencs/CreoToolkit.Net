namespace CreoToolkit.Sdk;

/// <summary>
/// 3D 无限直线值类型(点 + 方向, 不可变, 值语义)。
/// <para>
/// 表示无限直线 <c>{ Origin + t · Direction : t ∈ ℝ }</c>。
/// <see cref="Direction"/> <b>默认假设单位长</b>;非单位长由实例算子内部 <c>d/(d·d)</c> 公式抗未归一,
/// 但工厂 <see cref="FromTwoPoints"/> / <see cref="FromPointAndDirection"/> 会强制归一以省运行时除法。
/// </para>
/// <para>
/// 两点构造(对齐旧仓 Ptk3dLine 心智)经 <see cref="FromTwoPoints"/>;
/// 业务真要"两端点有限段"语义(裁剪/碰撞),请按需扩 <c>CreoSegment3</c>(本类型不承担)。
/// </para>
/// <para>
/// <b>判等警告</b>:<c>record struct</c> 的 <c>==</c> 是严格逐位浮点比较,业务请用 <see cref="AreEqual"/>(字段级 1e-12)。
/// 几何同一直线判定请用 <see cref="IsCollinearWith"/>。
/// </para>
/// </summary>
public readonly record struct CreoLine3(CreoVec3 Origin, CreoVec3 Direction)
{
    // ---- 静态工厂 ----

    /// <summary>两点构造:Origin=start, Direction=(end-start).Normalize()。两点重合(|end-start|&lt;1e-12)抛 <see cref="InvalidOperationException"/>。</summary>
    public static CreoLine3 FromTwoPoints(CreoVec3 start, CreoVec3 end)
    {
        var d = end - start;
        if (d.Length() < 1e-12)
            throw new InvalidOperationException("Cannot construct line: start and end coincide.");
        return new CreoLine3(start, d.Normalize());
    }

    /// <summary>点 + 方向构造,强制归一 Direction。零方向抛 <see cref="InvalidOperationException"/>。</summary>
    public static CreoLine3 FromPointAndDirection(CreoVec3 origin, CreoVec3 direction)
    {
        if (direction.Length() < 1e-12)
            throw new InvalidOperationException("Cannot construct line: direction is zero vector.");
        return new CreoLine3(origin, direction.Normalize());
    }

    /// <summary>字段级容差判等(逐分量 Origin + Direction 比较)。<b>不</b>做几何同一直线判定。</summary>
    public static bool AreEqual(CreoLine3 a, CreoLine3 b, double tolerance = 1e-12)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return CreoVec3.AreEqual(a.Origin, b.Origin, tolerance)
            && CreoVec3.AreEqual(a.Direction, b.Direction, tolerance);
    }

    // ---- 实例操作 ----

    /// <summary>点到无限直线的垂直距离。
    /// <para>算法:<c>|(p - Origin) × Direction| / |Direction|</c>(叉积面积法)。
    /// Direction 零向量抛 <see cref="InvalidOperationException"/>。</para></summary>
    public double DistanceTo(CreoVec3 point)
    {
        double dLen = Direction.Length();
        if (dLen < 1e-12)
            throw new InvalidOperationException("Line direction is zero vector; distance undefined.");
        return (point - Origin).Cross(Direction).Length() / dLen;
    }

    /// <summary>点是否在直线上(<c>DistanceTo(point) ≤ tolerance</c>)。Direction 零向量抛。</summary>
    public bool Contains(CreoVec3 point, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return DistanceTo(point) <= tolerance;
    }

    /// <summary>点到直线的垂足 = <c>Origin + ((p-Origin)·Direction)/(Direction·Direction) · Direction</c>。
    /// Direction 零向量抛。</summary>
    public CreoVec3 ClosestPointTo(CreoVec3 point)
    {
        double dd = Direction.Dot(Direction);
        if (dd < 1e-24)
            throw new InvalidOperationException("Line direction is zero vector; projection undefined.");
        double t = (point - Origin).Dot(Direction) / dd;
        return Origin + t * Direction;
    }

    /// <summary>两直线是否平行(Direction 平行,含反向)。Direction 零向量按 <see cref="CreoVec3.IsParallelTo"/> 数学约定返 true。</summary>
    public bool IsParallelTo(CreoLine3 other, double tolerance = 1e-9)
        => Direction.IsParallelTo(other.Direction, tolerance);

    /// <summary>几何同一直线判定:Direction 平行 + <paramref name="other"/>.Origin 落在 this 上。</summary>
    public bool IsCollinearWith(CreoLine3 other, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        if (!Direction.IsParallelTo(other.Direction, tolerance)) return false;
        return Contains(other.Origin, tolerance);
    }
}
