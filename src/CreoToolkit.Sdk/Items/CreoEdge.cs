using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 边(OHandle 衍生;session-bound)。
/// </summary>
public sealed class CreoEdge : CreoModelItem
{
    internal CreoEdge(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>取边的类型安全几何类型；不可读返回 null，未知 PTC 值保留 raw code。</summary>
    public CreoGeometryEntityType? GetGeometryType()
    {
        var raw = Session.Run(n => n.EdgeTypeGet(Ref));
        CreoGeometryEntityType? result = raw is { } value
            ? CreoGeometryEntityType.FromRaw(value)
            : null;
        CreoSdkLog.Trace("edge", "gettype",
            new { model = Ref.Model.Name, id = Ref.Id, type = result?.ToString(), raw });
        return result;
    }

    /// <summary>取边类型的原始 <c>pro_ent_type</c> code。</summary>
    [Obsolete("Use GetGeometryType(); RawValue is available only when interop with an unknown native code is required.")]
    public int? GetEdgeType()
        => GetGeometryType()?.RawValue;

    /// <summary>取边长度(<c>ProEdgeLengthEval</c>);不可读返回 null。</summary>
    public double? GetLength()
    {
        var result = Session.Run(n => n.EdgeLengthGet(Ref));
        CreoSdkLog.Trace("edge", "getlength",
            new { model = Ref.Model.Name, id = Ref.Id, length = result });
        return result;
    }

    /// <summary>取边的两个相邻面(<c>ProEdgeNeighborsGet</c>);单侧边 Surface2 为 null,不可读两者均 null。</summary>
    public (CreoSurface? Surface1, CreoSurface? Surface2) GetNeighborSurfaces()
    {
        var (f1, f2) = Session.Run(n => n.EdgeNeighborSurfacesGet(Ref));
        CreoSdkLog.Trace("edge", "getneighbors",
            new { model = Ref.Model.Name, id = Ref.Id, surface1 = f1?.Id, surface2 = f2?.Id });
        var s1 = f1 is not null ? new CreoSurface(Session, f1.Value) : null;
        var s2 = f2 is not null ? new CreoSurface(Session, f2.Value) : null;
        return (s1, s2);
    }
}
