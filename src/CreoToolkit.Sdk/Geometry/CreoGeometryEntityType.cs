namespace CreoToolkit.Sdk;

/// <summary>
/// Type-safe value returned by <c>ProEdgeTypeGet</c>/<c>ProCurveTypeGet</c>.
/// Unknown future PTC values are preserved in <see cref="RawValue"/>.
/// </summary>
public readonly record struct CreoGeometryEntityType(int RawValue)
{
    public static CreoGeometryEntityType None { get; } = new(-1);
    public static CreoGeometryEntityType Point { get; } = new(1);
    public static CreoGeometryEntityType Line { get; } = new(2);
    public static CreoGeometryEntityType Arc { get; } = new(3);
    public static CreoGeometryEntityType Text { get; } = new(6);
    public static CreoGeometryEntityType Arrow { get; } = new(8);
    public static CreoGeometryEntityType Circle { get; } = new(10);
    public static CreoGeometryEntityType Spline { get; } = new(12);
    public static CreoGeometryEntityType BSpline { get; } = new(19);
    public static CreoGeometryEntityType Ellipse { get; } = new(30);
    public static CreoGeometryEntityType Polygon { get; } = new(40);
    public static CreoGeometryEntityType CompositeCurve { get; } = new(41);
    public static CreoGeometryEntityType SurfaceCurve { get; } = new(56);
    public static CreoGeometryEntityType ParametricCurve { get; } = new(62);

    /// <summary>Creates a value without discarding unknown future native codes.</summary>
    public static CreoGeometryEntityType FromRaw(int rawValue) => new(rawValue);

    public bool IsKnown => RawValue is -1 or 1 or 2 or 3 or 6 or 8 or 10 or 12 or 19 or 30 or 40 or 41 or 56 or 62;

    public override string ToString() => RawValue switch
    {
        -1 => nameof(None),
        1 => nameof(Point),
        2 => nameof(Line),
        3 => nameof(Arc),
        6 => nameof(Text),
        8 => nameof(Arrow),
        10 => nameof(Circle),
        12 => nameof(Spline),
        19 => nameof(BSpline),
        30 => nameof(Ellipse),
        40 => nameof(Polygon),
        41 => nameof(CompositeCurve),
        56 => nameof(SurfaceCurve),
        62 => nameof(ParametricCurve),
        _ => $"Unknown({RawValue})",
    };
}
