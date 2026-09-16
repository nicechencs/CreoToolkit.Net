namespace CreoToolkit.Sdk;

/// <summary>形位公差类型(值 = ProGtol.h ProGtolType 真值,不引入内部值)。
/// 排除 PROGTOLTYPE_UNKNOWN(0)。</summary>
public enum CreoGtolType
{
    /// <summary>PROGTOLTYPE_STRAIGHTNESS = 1。直线度。</summary>
    Straightness = 1,
    /// <summary>PROGTOLTYPE_FLATNESS = 2。平面度。</summary>
    Flatness = 2,
    /// <summary>PROGTOLTYPE_CIRCULAR = 3。圆度。</summary>
    Circular = 3,
    /// <summary>PROGTOLTYPE_CYLINDRICAL = 4。圆柱度。</summary>
    Cylindrical = 4,
    /// <summary>PROGTOLTYPE_LINE = 11。线轮廓度。</summary>
    Line = 11,
    /// <summary>PROGTOLTYPE_SURFACE = 12。面轮廓度。</summary>
    Surface = 12,
    /// <summary>PROGTOLTYPE_ANGULAR = 21。倾斜度。</summary>
    Angular = 21,
    /// <summary>PROGTOLTYPE_PERPENDICULAR = 22。垂直度。</summary>
    Perpendicular = 22,
    /// <summary>PROGTOLTYPE_PARALLEL = 23。平行度。</summary>
    Parallel = 23,
    /// <summary>PROGTOLTYPE_POSITION = 31。位置度。</summary>
    Position = 31,
    /// <summary>PROGTOLTYPE_CONCENTRICITY = 32。同心度。</summary>
    Concentricity = 32,
    /// <summary>PROGTOLTYPE_SYMMETRY = 35。对称度。</summary>
    Symmetry = 35,
    /// <summary>PROGTOLTYPE_CIRCULAR_RUNOUT = 41。圆跳动。</summary>
    CircularRunout = 41,
    /// <summary>PROGTOLTYPE_TOTAL_RUNOUT = 42。全跳动。</summary>
    TotalRunout = 42,
}
