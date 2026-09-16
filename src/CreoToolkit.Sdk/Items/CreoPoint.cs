using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 基准点(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoPoint : CreoModelItem
{
    internal CreoPoint(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>基准点 3D 坐标(直绑 <c>ProPointInit + ProPointCoordGet</c>, DATA);
    /// owner 不在会话或点 id 不存在 → null。返 <see cref="CreoVec3"/> 在 owner 模型坐标系下。</summary>
    public CreoVec3? GetCoord()
    {
        var arr = Session.Run(n => n.PointCoordGet(Ref));
        CreoSdkLog.Trace("point", "getcoord",
            new { model = Ref.Model.Name, id = Ref.Id, found = arr is not null });
        return arr is null ? null : CreoVec3.FromArray(arr);
    }
}
