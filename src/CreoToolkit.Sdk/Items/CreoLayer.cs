using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 层(DHandle 衍生;session-bound;与 <see cref="CreoLayers"/> facade 共存)。
/// </summary>
public sealed class CreoLayer : CreoModelItem
{
    internal CreoLayer(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>列出层中包含的所有模型条目。</summary>
    public IReadOnlyList<ICreoModelItem> ListItems()
    {
        var refs = Session.Run(n => n.LayerItemsList(Ref));
        CreoSdkLog.Trace("layer", "listitems", new { model = Ref.Model.Name, id = Ref.Id, count = refs.Count });
        if (refs.Count == 0) return Array.Empty<ICreoModelItem>();
        var result = new ICreoModelItem[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = CreoModelItem.CreateFromRef(Session, refs[i]);
        return result;
    }
}
