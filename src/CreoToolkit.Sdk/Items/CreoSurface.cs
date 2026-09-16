using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 面(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoSurface : CreoModelItem
{
    internal CreoSurface(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>取面类型 int(<c>ProSurfaceInit + ProSurfaceTypeGet</c>);不可读返回 null。
    /// 返回值对应 <c>pro_srf_type</c> enum:PLANE=34/CYL=36/CONE=37/TORUS=38/COONS=39/SPL=40/FIL=41/RUL=42/REV=43/TABCYL=44/B_SPL=45/FOREIGN=46/CYL_SPL=48/SPL2DER=50。
    /// 暴露的 L3 缺口。</summary>
    public int? GetSurfaceType()
    {
        var result = Session.Run(n => n.SurfaceTypeGet(Ref));
        CreoSdkLog.Trace("surface", "gettype",
            new { model = Ref.Model.Name, id = Ref.Id, type = result });
        return result;
    }

    /// <summary>取 PLANE 类型面的 <see cref="CreoPlane"/>(直绑 ProSurfacedataGet + ProPlanedataGet)。
    /// <para>非平面 / 不可读返 null。Origin=Pro origin,Normal=Pro e3(<b>不归一</b>,保留 Pro 原值;
    /// 调用方需要单位长经 <see cref="CreoPlane.WithNormalizedNormal"/>)。
    /// 面内基 e1/e2 当前丢弃(future 扩面 uv 基时回填,见 <see cref="SurfacePlaneData"/>)。</para></summary>
    public CreoPlane? GetPlane()
    {
        var data = Session.Run(n => n.SurfacePlaneDataGet(Ref));
        CreoSdkLog.Trace("surface", "getplane",
            new { model = Ref.Model.Name, id = Ref.Id, found = data is not null });
        return data is null ? null : CreoPlane.FromPtcPlane(data.Value.Origin, data.Value.E3);
    }

    /// <summary>列举面上所有边(嵌套 <c>ProSurfaceContourVisit</c> + <c>ProContourEdgeVisit</c>);
    /// 同一 edge 跨 contour 会去重(record struct 值语义)。无 edge / 不可读返空列表。
    /// <para>触达 <see cref="CreoEdge.GetLength"/>/<see cref="CreoEdge.GetTypeCode"/>/
    /// <see cref="CreoEdge.GetNeighborSurfaces"/> 的 top-level 入口。</para></summary>
    public IReadOnlyList<CreoEdge> ListEdges()
    {
        var refs = Session.Run(n => n.SurfaceEdgeList(Ref));
        CreoSdkLog.Trace("surface", "listedges",
            new { model = Ref.Model.Name, id = Ref.Id, count = refs.Count });
        if (refs.Count == 0) return Array.Empty<CreoEdge>();
        var result = new CreoEdge[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = new CreoEdge(Session, refs[i]);
        return result;
    }
}
