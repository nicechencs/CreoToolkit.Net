namespace CreoToolkit.Sdk;

/// <summary>
/// 3D 向量 / 点值类型(行优先, 不可变, 值语义)。
/// <para>
/// 与 Pro/Toolkit native 边界 <c>double[3]</c> 互转走 <see cref="FromArray"/>/<see cref="ToArray"/>;
/// 几何算子(Dot/Cross/Length/Normalize/Transform)挂 instance, 工厂(Zero/UnitX/Y/Z)挂 static。
/// </para>
/// <para>
/// <b>判等警告</b>:<c>record struct</c> 的 <c>==</c> / <see cref="Equals"/> 是**严格浮点比较**(逐位匹配),
/// 不带容差 — 业务代码请用 <see cref="AreEqual(CreoVec3, CreoVec3, double)"/> 做近似判等,
/// 不要直接 <c>vecA == vecB</c>(累计漂移会让看起来相等的向量判 false)。
/// </para>
/// </summary>
public readonly record struct CreoVec3(double X, double Y, double Z)
{
    /// <summary>零向量 (0,0,0)。</summary>
    public static CreoVec3 Zero => new(0.0, 0.0, 0.0);

    /// <summary>x 轴单位向量 (1,0,0)。</summary>
    public static CreoVec3 UnitX => new(1.0, 0.0, 0.0);

    /// <summary>y 轴单位向量 (0,1,0)。</summary>
    public static CreoVec3 UnitY => new(0.0, 1.0, 0.0);

    /// <summary>z 轴单位向量 (0,0,1)。</summary>
    public static CreoVec3 UnitZ => new(0.0, 0.0, 1.0);

    /// <summary>从 3-double 数组构造;长度 ≠ 3 抛 <see cref="ArgumentException"/>。</summary>
    public static CreoVec3 FromArray(double[] arr)
    {
        ThrowUtil.IfNull(arr);
        if (arr.Length != 3)
            throw new ArgumentException($"Expected 3-double vector, got {arr.Length}.", nameof(arr));
        return new CreoVec3(arr[0], arr[1], arr[2]);
    }

    /// <summary>装回 3-double 数组(用于 native 边界 / 兼容旧代码)。</summary>
    public double[] ToArray() => new[] { X, Y, Z };

    /// <summary>逐元素容差判等(|a-b| ≤ tolerance);<c>==</c> 严格浮点比较不安全,业务请用本方法。</summary>
    public static bool AreEqual(CreoVec3 a, CreoVec3 b, double tolerance = 1e-12)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        return Math.Abs(a.X - b.X) <= tolerance
            && Math.Abs(a.Y - b.Y) <= tolerance
            && Math.Abs(a.Z - b.Z) <= tolerance;
    }

    /// <summary>点积 <c>this · other = X·X' + Y·Y' + Z·Z'</c>。</summary>
    public double Dot(CreoVec3 other) => X * other.X + Y * other.Y + Z * other.Z;

    /// <summary>叉积 <c>this × other</c>(右手定则)。<c>UnitX.Cross(UnitY) = UnitZ</c>。</summary>
    public CreoVec3 Cross(CreoVec3 other) => new(
        Y * other.Z - Z * other.Y,
        Z * other.X - X * other.Z,
        X * other.Y - Y * other.X);

    /// <summary>欧几里得长度 <c>√(X² + Y² + Z²)</c>。</summary>
    public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>归一化(单位向量)。零向量(|v| &lt; 1e-12)抛 <see cref="InvalidOperationException"/>。</summary>
    public CreoVec3 Normalize()
    {
        double len = Length();
        if (len < 1e-12)
            throw new InvalidOperationException($"Cannot normalize: |v|={len:G3} < 1e-12 (zero vector).");
        double inv = 1.0 / len;
        return new(X * inv, Y * inv, Z * inv);
    }

    /// <summary>作为<b>点</b>经矩阵变换(齐次 w=1, 平移参与;对齐 <c>ProPntTrfEval</c>)。
    /// 等价 <see cref="CreoMat4.TransformPoint"/>(<c>matrix.TransformPoint(this)</c>)。</summary>
    public CreoVec3 Transform(CreoMat4 matrix) => new(
        X * matrix.M00 + Y * matrix.M10 + Z * matrix.M20 + matrix.M30,
        X * matrix.M01 + Y * matrix.M11 + Z * matrix.M21 + matrix.M31,
        X * matrix.M02 + Y * matrix.M12 + Z * matrix.M22 + matrix.M32);

    /// <summary>作为<b>向量</b>经矩阵变换(齐次 w=0, 平移不参与;对齐 <c>ProVectorTrfEval</c>)。
    /// 等价 <see cref="CreoMat4.TransformDirection"/>(<c>matrix.TransformDirection(this)</c>)。</summary>
    public CreoVec3 TransformDirection(CreoMat4 matrix) => new(
        X * matrix.M00 + Y * matrix.M10 + Z * matrix.M20,
        X * matrix.M01 + Y * matrix.M11 + Z * matrix.M21,
        X * matrix.M02 + Y * matrix.M12 + Z * matrix.M22);

    // ---- 算术运算符 ----
    // 值语义不可变,所有运算符返新值不改 this;链式 (a+b)*0.5 安全。
    // 对齐 System.Numerics.Vector3 命名;故意不引入 *(Vec3, Vec3) 逐分量乘(几何上歧义,Dot/Cross 显式)。

    /// <summary>逐分量加 a+b。</summary>
    public static CreoVec3 operator +(CreoVec3 a, CreoVec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>逐分量减 a-b。常用于「点 - 点 = 方向向量」。</summary>
    public static CreoVec3 operator -(CreoVec3 a, CreoVec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>一元负号:(-X, -Y, -Z)。</summary>
    public static CreoVec3 operator -(CreoVec3 v) => new(-v.X, -v.Y, -v.Z);

    /// <summary>标量乘 v*s(缩放)。</summary>
    public static CreoVec3 operator *(CreoVec3 v, double scalar) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>标量乘 s*v(对称,交换律)。</summary>
    public static CreoVec3 operator *(double scalar, CreoVec3 v) => new(v.X * scalar, v.Y * scalar, v.Z * scalar);

    /// <summary>标量除 v/s。s=0 抛 <see cref="DivideByZeroException"/>(.NET 默认浮点除 ±Inf 不抛,此处显式拦)。</summary>
    public static CreoVec3 operator /(CreoVec3 v, double scalar)
    {
        if (scalar == 0.0)
            throw new DivideByZeroException("CreoVec3 / 0 未定义。");
        double inv = 1.0 / scalar;
        return new(v.X * inv, v.Y * inv, v.Z * inv);
    }

    // ---- 距离 ----

    /// <summary>点-点欧氏距离 <c>|this - other|</c>。</summary>
    public double DistanceTo(CreoVec3 other) => Math.Sqrt(DistanceSquaredTo(other));

    /// <summary>距离平方(免开方,用于排序/比较/KNN 等热点)。</summary>
    public double DistanceSquaredTo(CreoVec3 other)
    {
        double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    // ---- 平行/垂直 ----
    // 用相对容差(cross/dot 归一到 sin/cos θ 量纲),大向量与单位向量同 tol 行为一致。
    // 零向量数学约定:既平行又垂直于所有向量(0 = k·v ∧ 0·v = 0 同时成立)→ 两个谓词都返 true。

    /// <summary>是否平行(含反向、含零向量退化)。
    /// <para>算法:<c>|this × other| ≤ tolerance · |this| · |other|</c>(相对叉积,sin θ 量纲)。
    /// 任一向量长度 &lt; 1e-12 视为零向量,返 <c>true</c>(数学约定:零向量与任何向量共线)。</para></summary>
    public bool IsParallelTo(CreoVec3 other, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        double la = Length(), lb = other.Length();
        if (la < 1e-12 || lb < 1e-12) return true;
        return Cross(other).Length() <= tolerance * la * lb;
    }

    /// <summary>是否垂直。
    /// <para>算法:<c>|this · other| ≤ tolerance · |this| · |other|</c>(相对点积,cos θ 量纲)。
    /// 任一向量长度 &lt; 1e-12 视为零向量,返 <c>true</c>(数学约定:零向量与任何向量正交)。</para></summary>
    public bool IsPerpendicularTo(CreoVec3 other, double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        double la = Length(), lb = other.Length();
        if (la < 1e-12 || lb < 1e-12) return true;
        return Math.Abs(Dot(other)) <= tolerance * la * lb;
    }
}
