using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 基准曲线(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoCurve : CreoModelItem
{
    internal CreoCurve(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>取曲线长度(<c>ProCurveLengthEval</c>);不可读返回 null。</summary>
    public double? GetLength()
    {
        var result = Session.Run(n => n.CurveLengthGet(Ref));
        CreoSdkLog.Trace("curve", "getlength",
            new { model = Ref.Model.Name, id = Ref.Id, length = result });
        return result;
    }

    /// <summary>取曲线的类型安全几何类型；不可读返回 null，未知 PTC 值保留 raw code。</summary>
    public CreoGeometryEntityType? GetGeometryType()
    {
        var raw = Session.Run(n => n.CurveTypeGet(Ref));
        CreoGeometryEntityType? result = raw is { } value
            ? CreoGeometryEntityType.FromRaw(value)
            : null;
        CreoSdkLog.Trace("curve", "gettype",
            new { model = Ref.Model.Name, id = Ref.Id, type = result?.ToString(), raw });
        return result;
    }

    /// <summary>取曲线类型的原始 <c>pro_ent_type</c> code。</summary>
    [Obsolete("Use GetGeometryType(); RawValue is available only when interop with an unknown native code is required.")]
    public int? GetCurveType()
        => GetGeometryType()?.RawValue;
}
