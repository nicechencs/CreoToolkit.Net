using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 轴(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoAxis : CreoModelItem
{
    internal CreoAxis(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>取轴线段两端点(直绑 <c>ProAxisDataGet → ProLinedata</c>, DATA copy-out);
    /// 方向 = End2 - End1(未归一化)。不可读返 null。</summary>
    public (CreoVec3 End1, CreoVec3 End2)? GetLineData()
    {
        var data = Session.Run(n => n.AxisLineDataGet(Ref));
        CreoSdkLog.Trace("axis", "getlinedata",
            new { model = Ref.Model.Name, id = Ref.Id, found = data is not null });
        return data is null
            ? null
            : (CreoVec3.FromArray(data.Value.End1), CreoVec3.FromArray(data.Value.End2));
    }

    /// <summary>取拥有该轴的面(直绑 <c>ProAxisSurfaceGet</c>);无关联面返 null。</summary>
    public CreoSurface? GetOwnerSurface()
    {
        var surfRef = Session.Run(n => n.AxisOwnerSurfaceGet(Ref));
        CreoSdkLog.Trace("axis", "getownersurface",
            new { model = Ref.Model.Name, id = Ref.Id, found = surfRef is not null });
        return surfRef is null ? null : new CreoSurface(Session, surfRef.Value);
    }
}
