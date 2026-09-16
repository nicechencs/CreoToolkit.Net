using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 面片体(OHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoQuilt : CreoModelItem
{
    internal CreoQuilt(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>列举面片体包含的所有面(直绑 <c>ProQuiltSurfaceVisit</c>);无面返空列表。</summary>
    public IReadOnlyList<CreoSurface> ListSurfaces()
    {
        var refs = Session.Run(n => n.QuiltSurfaceList(Ref));
        CreoSdkLog.Trace("quilt", "listsurfaces",
            new { model = Ref.Model.Name, id = Ref.Id, count = refs.Count });
        if (refs.Count == 0) return Array.Empty<CreoSurface>();
        var result = new CreoSurface[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = new CreoSurface(Session, refs[i]);
        return result;
    }

    /// <summary>计算面片体体积(直绑 <c>ProQuiltVolumeEval</c>);不可算返 null。</summary>
    public double? GetVolume()
    {
        var result = Session.Run(n => n.QuiltVolumeEval(Ref));
        CreoSdkLog.Trace("quilt", "getvolume",
            new { model = Ref.Model.Name, id = Ref.Id, volume = result });
        return result;
    }

    /// <summary>判断面片体是否为备份几何(直绑 <c>ProQuiltIsBackupGeometry</c>);不可判返 null。</summary>
    public bool? IsBackupGeometry()
    {
        var result = Session.Run(n => n.QuiltIsBackupGeometry(Ref));
        CreoSdkLog.Trace("quilt", "isbackupgeometry",
            new { model = Ref.Model.Name, id = Ref.Id, isBackup = result });
        return result;
    }
}
