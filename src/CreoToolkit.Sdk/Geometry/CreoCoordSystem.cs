namespace CreoToolkit.Sdk;

/// <summary>
/// 坐标系数据快照(原点 + 3 轴方向, 不可变, 值语义)。
/// <para>
/// 与 <see cref="CreoMat4"/> 的关系:本类型是 4×4 矩阵的「几何视图」
/// (前 3 行 = X/Y/Z 轴, 第 4 行 = Origin)。互转经 <see cref="FromMatrix"/> / <see cref="ToMatrix"/>,
/// 语义对齐 <see cref="CreoMat4.FromAxes"/>。
/// </para>
/// <para>
/// <b>不强制正交</b>:本类型只搬数据,正交性由调用方判(<see cref="ToMatrix"/>.<see cref="CreoMat4.IsOrthonormal"/>)。
/// </para>
/// <para>
/// <b>判等警告</b>:<c>record struct</c> 的 <c>==</c> 是严格逐位浮点比较,业务请用 <see cref="AreEqual"/>。
/// </para>
/// </summary>
public readonly record struct CreoCoordSystem(
    CreoVec3 Origin,
    CreoVec3 XAxis,
    CreoVec3 YAxis,
    CreoVec3 ZAxis)
{
    /// <summary>从 4×4 行优先矩阵抽取:前 3 行=X/Y/Z 轴, 第 4 行=Origin(对齐 <see cref="CreoMat4.FromAxes"/> 反操作)。</summary>
    public static CreoCoordSystem FromMatrix(CreoMat4 m) => new(
        Origin: new CreoVec3(m.M30, m.M31, m.M32),
        XAxis:  new CreoVec3(m.M00, m.M01, m.M02),
        YAxis:  new CreoVec3(m.M10, m.M11, m.M12),
        ZAxis:  new CreoVec3(m.M20, m.M21, m.M22));

    /// <summary>组装 4×4 行优先矩阵(等价 <see cref="CreoMat4.FromAxes"/>(XAxis, YAxis, ZAxis, Origin))。</summary>
    public CreoMat4 ToMatrix() => CreoMat4.FromAxes(XAxis, YAxis, ZAxis, Origin);

    /// <summary>字段级容差判等(逐分量 Origin/XAxis/YAxis/ZAxis 比较)。</summary>
    public static bool AreEqual(CreoCoordSystem a, CreoCoordSystem b, double tolerance = 1e-12)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return CreoVec3.AreEqual(a.Origin, b.Origin, tolerance)
            && CreoVec3.AreEqual(a.XAxis,  b.XAxis,  tolerance)
            && CreoVec3.AreEqual(a.YAxis,  b.YAxis,  tolerance)
            && CreoVec3.AreEqual(a.ZAxis,  b.ZAxis,  tolerance);
    }
}
