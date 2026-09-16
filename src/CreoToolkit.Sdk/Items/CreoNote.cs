using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 注释(DHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoNote : CreoModelItem
{
    internal CreoNote(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>读取本 Note 的 URL(DATA, ProNoteURLWstringGet 直绑)。
    /// 模型不可解析、note 无 URL 或不可读返回 null(→null)。
    /// </summary>
    public string? GetUrl() => Session.Run(n => n.NoteUrlGet(Ref));
}
