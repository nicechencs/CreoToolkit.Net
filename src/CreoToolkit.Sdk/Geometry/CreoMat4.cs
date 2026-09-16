namespace CreoToolkit.Sdk;

/// <summary>
/// 4×4 行优先矩阵值类型(不可变, 值语义, 与 Pro/Toolkit <c>ProMatrix[4][4]</c> 内存布局一致)。
/// <para>
/// 字段命名:<c>Mij</c> = ProMatrix[i][j], i=行 j=列, 行优先。
/// 前 3 行 = 方向余弦(x/y/z 轴), 第 4 行 = 平移/原点, 第 4 列齐次 [i][3] = i&lt;3 ? 0 : 1。
/// </para>
/// <para>
/// 变换语义(对齐 <c>ProPntTrfEval</c>/<c>ProVectorTrfEval</c>):<c>out = in_row × matrix</c>;
/// 复合 <c>M_total = M_first × M_second</c> 表先 first 后 second。
/// </para>
/// <para>
/// <b>不要参考旧 <c>PtkMatrix</c></b>(<c>old/CreoCSharp/Pkg/Math</c>):旧版第 4 列=origin + column-vector
/// 约定与 Pro/Toolkit row-vector 约定不一致。
/// </para>
/// <para>
/// <b>判等警告</b>:<c>record struct</c> 的 <c>==</c> / <see cref="Equals"/> 是严格浮点比较,
/// 业务请用 <see cref="AreEqual(CreoMat4, CreoMat4, double)"/>。
/// </para>
/// </summary>
public readonly record struct CreoMat4(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23,
    double M30, double M31, double M32, double M33)
{
    // ---- 静态工厂 ----

    /// <summary>4×4 单位矩阵。</summary>
    public static CreoMat4 Identity() => new(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0);

    /// <summary>由 3 个轴向量 + 原点构造行优先 4×4 矩阵(语义对齐 <c>ProMatrixInit</c>)。</summary>
    public static CreoMat4 FromAxes(CreoVec3 xAxis, CreoVec3 yAxis, CreoVec3 zAxis, CreoVec3 origin) => new(
        xAxis.X,  xAxis.Y,  xAxis.Z,  0.0,
        yAxis.X,  yAxis.Y,  yAxis.Z,  0.0,
        zAxis.X,  zAxis.Y,  zAxis.Z,  0.0,
        origin.X, origin.Y, origin.Z, 1.0);

    /// <summary>纯平移矩阵(单位旋转 + 给定原点)。</summary>
    public static CreoMat4 Translation(CreoVec3 offset) => new(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        offset.X, offset.Y, offset.Z, 1.0);

    /// <summary>绕 x 轴右手定则旋转(弧度);row-vector 约定。</summary>
    public static CreoMat4 RotationX(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new(
            1.0, 0.0, 0.0, 0.0,
            0.0,   c,   s, 0.0,
            0.0,  -s,   c, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>绕 y 轴右手定则旋转(弧度);row-vector 约定。</summary>
    public static CreoMat4 RotationY(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new(
              c, 0.0,  -s, 0.0,
            0.0, 1.0, 0.0, 0.0,
              s, 0.0,   c, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>绕 z 轴右手定则旋转(弧度);row-vector 约定。<c>UnitX 经 RotationZ(π/2) → UnitY</c>。</summary>
    public static CreoMat4 RotationZ(double radians)
    {
        double c = Math.Cos(radians);
        double s = Math.Sin(radians);
        return new(
              c,   s, 0.0, 0.0,
             -s,   c, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    /// <summary>从 16-double 行优先数组构造;长度 ≠ 16 抛 <see cref="ArgumentException"/>。</summary>
    public static CreoMat4 FromArray(double[] arr)
    {
        ThrowUtil.IfNull(arr);
        if (arr.Length != 16)
            throw new ArgumentException($"Expected 16-double row-major matrix, got {arr.Length}.", nameof(arr));
        return new CreoMat4(
            arr[0], arr[1], arr[2], arr[3],
            arr[4], arr[5], arr[6], arr[7],
            arr[8], arr[9], arr[10], arr[11],
            arr[12], arr[13], arr[14], arr[15]);
    }

    /// <summary>装回 16-double 行优先数组(用于 native 边界 / 兼容旧代码)。</summary>
    public double[] ToArray() => new[]
    {
        M00, M01, M02, M03,
        M10, M11, M12, M13,
        M20, M21, M22, M23,
        M30, M31, M32, M33,
    };

    /// <summary>逐元素容差判等(|a-b| ≤ tolerance, 16 项);<c>==</c> 严格浮点比较不安全,业务请用本方法。</summary>
    public static bool AreEqual(CreoMat4 a, CreoMat4 b, double tolerance = 1e-12)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        double t = tolerance;
        return Math.Abs(a.M00 - b.M00) <= t && Math.Abs(a.M01 - b.M01) <= t
            && Math.Abs(a.M02 - b.M02) <= t && Math.Abs(a.M03 - b.M03) <= t
            && Math.Abs(a.M10 - b.M10) <= t && Math.Abs(a.M11 - b.M11) <= t
            && Math.Abs(a.M12 - b.M12) <= t && Math.Abs(a.M13 - b.M13) <= t
            && Math.Abs(a.M20 - b.M20) <= t && Math.Abs(a.M21 - b.M21) <= t
            && Math.Abs(a.M22 - b.M22) <= t && Math.Abs(a.M23 - b.M23) <= t
            && Math.Abs(a.M30 - b.M30) <= t && Math.Abs(a.M31 - b.M31) <= t
            && Math.Abs(a.M32 - b.M32) <= t && Math.Abs(a.M33 - b.M33) <= t;
    }

    // ---- 实例操作 ----

    /// <summary>按 (row, col) 索引读元素(0..3, 0..3)。越界抛 <see cref="ArgumentOutOfRangeException"/>。</summary>
    public double this[int row, int col]
    {
        get
        {
            if ((uint)row > 3u)
                throw new ArgumentOutOfRangeException(nameof(row), row, $"row must be 0..3, got {row}.");
            if ((uint)col > 3u)
                throw new ArgumentOutOfRangeException(nameof(col), col, $"col must be 0..3, got {col}.");
            return (row * 4 + col) switch
            {
                0  => M00,  1 => M01,  2 => M02,  3 => M03,
                4  => M10,  5 => M11,  6 => M12,  7 => M13,
                8  => M20,  9 => M21, 10 => M22, 11 => M23,
                _  => /* 12..15 */ (row * 4 + col) switch { 12 => M30, 13 => M31, 14 => M32, _ => M33 },
            };
        }
    }

    /// <summary>变换点(齐次 w=1);等价 <c>vec.Transform(this)</c>(<see cref="CreoVec3.Transform"/>)。</summary>
    public CreoVec3 TransformPoint(CreoVec3 point) => point.Transform(this);

    /// <summary>变换向量(齐次 w=0);等价 <c>vec.TransformDirection(this)</c>。</summary>
    public CreoVec3 TransformDirection(CreoVec3 vector) => vector.TransformDirection(this);

    /// <summary>矩阵相乘 <c>result = this × other</c>(先 this 再 other)。</summary>
    public CreoMat4 Multiply(CreoMat4 b) => new(
        M00 * b.M00 + M01 * b.M10 + M02 * b.M20 + M03 * b.M30,
        M00 * b.M01 + M01 * b.M11 + M02 * b.M21 + M03 * b.M31,
        M00 * b.M02 + M01 * b.M12 + M02 * b.M22 + M03 * b.M32,
        M00 * b.M03 + M01 * b.M13 + M02 * b.M23 + M03 * b.M33,

        M10 * b.M00 + M11 * b.M10 + M12 * b.M20 + M13 * b.M30,
        M10 * b.M01 + M11 * b.M11 + M12 * b.M21 + M13 * b.M31,
        M10 * b.M02 + M11 * b.M12 + M12 * b.M22 + M13 * b.M32,
        M10 * b.M03 + M11 * b.M13 + M12 * b.M23 + M13 * b.M33,

        M20 * b.M00 + M21 * b.M10 + M22 * b.M20 + M23 * b.M30,
        M20 * b.M01 + M21 * b.M11 + M22 * b.M21 + M23 * b.M31,
        M20 * b.M02 + M21 * b.M12 + M22 * b.M22 + M23 * b.M32,
        M20 * b.M03 + M21 * b.M13 + M22 * b.M23 + M23 * b.M33,

        M30 * b.M00 + M31 * b.M10 + M32 * b.M20 + M33 * b.M30,
        M30 * b.M01 + M31 * b.M11 + M32 * b.M21 + M33 * b.M31,
        M30 * b.M02 + M31 * b.M12 + M32 * b.M22 + M33 * b.M32,
        M30 * b.M03 + M31 * b.M13 + M32 * b.M23 + M33 * b.M33);

    /// <summary>转置。</summary>
    public CreoMat4 Transpose() => new(
        M00, M10, M20, M30,
        M01, M11, M21, M31,
        M02, M12, M22, M32,
        M03, M13, M23, M33);

    /// <summary>4×4 行列式。&gt;0 右手系 / &lt;0 左手系 / ≈0 奇异。</summary>
    public double Determinant()
    {
        // Cofactor 已带 sign(-1)^(i+j),沿第 0 行展开累加
        return M00 * Cofactor(0, 0) + M01 * Cofactor(0, 1)
             + M02 * Cofactor(0, 2) + M03 * Cofactor(0, 3);
    }

    /// <summary>通用 4×4 求逆(余子式 / 伴随 / 除行列式)。
    /// 仿射场景请优先用 <see cref="InverseAffine"/> — 快 ~50× 且数值稳定。
    /// |det| &lt; 1e-12 抛 <see cref="InvalidOperationException"/>(奇异)。</summary>
    public CreoMat4 Inverse()
    {
        double det = Determinant();
        if (Math.Abs(det) < 1e-12)
            throw new InvalidOperationException($"Matrix is singular (|det|={Math.Abs(det):G3} < 1e-12).");
        double invDet = 1.0 / det;
        // adjoint[j,i] = Cofactor(i,j);result[i,j] = Cofactor(j,i) * invDet
        return new CreoMat4(
            Cofactor(0, 0) * invDet, Cofactor(1, 0) * invDet, Cofactor(2, 0) * invDet, Cofactor(3, 0) * invDet,
            Cofactor(0, 1) * invDet, Cofactor(1, 1) * invDet, Cofactor(2, 1) * invDet, Cofactor(3, 1) * invDet,
            Cofactor(0, 2) * invDet, Cofactor(1, 2) * invDet, Cofactor(2, 2) * invDet, Cofactor(3, 2) * invDet,
            Cofactor(0, 3) * invDet, Cofactor(1, 3) * invDet, Cofactor(2, 3) * invDet, Cofactor(3, 3) * invDet);
    }

    /// <summary>仿射快速求逆(假设上 3×3 正交 + 第 4 行平移)。
    /// <para>M = [R | 0; t | 1] ⇒ M⁻¹ = [Rᵀ | 0; -t·Rᵀ | 1]。比通用 <see cref="Inverse"/> 快 ~50×。</para>
    /// <para><b>调用方负责保证上 3×3 正交</b>(Creo path/csys 默认满足;不确定先用
    /// <see cref="IsOrthonormal"/> 校验或 <see cref="MakeOrthonormal"/> 修正)。
    /// 非正交输入得错结果,本方法不校验、不抛。</para></summary>
    public CreoMat4 InverseAffine()
    {
        // Rᵀ
        double r00 = M00, r01 = M10, r02 = M20;
        double r10 = M01, r11 = M11, r12 = M21;
        double r20 = M02, r21 = M12, r22 = M22;
        // -t · Rᵀ
        double tx = M30, ty = M31, tz = M32;
        double nx = -(tx * r00 + ty * r10 + tz * r20);
        double ny = -(tx * r01 + ty * r11 + tz * r21);
        double nz = -(tx * r02 + ty * r12 + tz * r22);
        return new CreoMat4(
            r00, r01, r02, 0.0,
            r10, r11, r12, 0.0,
            r20, r21, r22, 0.0,
             nx,  ny,  nz, 1.0);
    }

    /// <summary>校验上 3×3 是否正交(各轴单位长 + 两两垂直, 容差内);第 4 列/第 4 行不参与。</summary>
    public bool IsOrthonormal(double tolerance = 1e-9)
    {
        if (tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "tolerance must be non-negative.");
        var x = new CreoVec3(M00, M01, M02);
        var y = new CreoVec3(M10, M11, M12);
        var z = new CreoVec3(M20, M21, M22);
        return Math.Abs(x.Length() - 1.0) <= tolerance
            && Math.Abs(y.Length() - 1.0) <= tolerance
            && Math.Abs(z.Length() - 1.0) <= tolerance
            && Math.Abs(x.Dot(y)) <= tolerance
            && Math.Abs(y.Dot(z)) <= tolerance
            && Math.Abs(x.Dot(z)) <= tolerance;
    }

    /// <summary>Gram-Schmidt 正交化上 3×3(对齐 <c>ProMatrixMakeOrthonormal</c>, native 未导出 → .NET 端实现)。
    /// 原 x 轴方向保留, y 减 x 投影后归一, z = x × y。第 4 行平移原样保留;
    /// 原 x 零向量或 y 平行 x 抛 <see cref="InvalidOperationException"/>。</summary>
    public CreoMat4 MakeOrthonormal()
    {
        var x = new CreoVec3(M00, M01, M02);
        if (x.Length() < 1e-12)
            throw new InvalidOperationException("Cannot orthonormalize: x-axis is zero vector.");
        var xN = x.Normalize();
        var y = new CreoVec3(M10, M11, M12);
        double yDotX = y.Dot(xN);
        var yProj = new CreoVec3(y.X - yDotX * xN.X, y.Y - yDotX * xN.Y, y.Z - yDotX * xN.Z);
        if (yProj.Length() < 1e-12)
            throw new InvalidOperationException("Cannot orthonormalize: y-axis is parallel to x-axis.");
        var yN = yProj.Normalize();
        var zN = xN.Cross(yN);
        return new CreoMat4(
            xN.X, xN.Y, xN.Z, 0.0,
            yN.X, yN.Y, yN.Z, 0.0,
            zN.X, zN.Y, zN.Z, 0.0,
            M30,  M31,  M32,  1.0);
    }

    // ---- private ----

    /// <summary>Cofactor (row, col) = sign·det(minor),sign=(-1)^(row+col)。
    /// 使用 stackalloc 避免 heap 分配(72 byte 栈;hot path 上零 GC 压力)。</summary>
    private double Cofactor(int row, int col)
    {
        Span<double> minor = stackalloc double[9];
        int idx = 0;
        for (int r = 0; r < 4; r++)
        {
            if (r == row) continue;
            for (int c = 0; c < 4; c++)
            {
                if (c == col) continue;
                minor[idx++] = this[r, c];
            }
        }
        double det3 = minor[0] * (minor[4] * minor[8] - minor[5] * minor[7])
                    - minor[1] * (minor[3] * minor[8] - minor[5] * minor[6])
                    + minor[2] * (minor[3] * minor[7] - minor[4] * minor[6]);
        return ((row + col) & 1) == 0 ? det3 : -det3;
    }
}
