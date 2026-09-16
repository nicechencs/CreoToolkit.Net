using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 参考尺寸(DHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoRefDimension : CreoModelItem
{
    internal CreoRefDimension(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>尺寸值（ProDimensionValueGet）；不可读返 null。</summary>
    public double? GetValue() => Session.Run(n => n.DimensionValueGet(Ref));

    /// <summary>尺寸类型 int（pro_dimensiontype 枚举）；不可读返 null。</summary>
    public int? GetTypeCode() => Session.Run(n => n.DimensionTypeGet(Ref));

    /// <summary>尺寸显示文本，含前缀/值/后缀/公差（ProDimensionTextWstringsGet）；不可读返 null。</summary>
    public string? GetDisplayText() => Session.Run(n => n.DimensionTextGet(Ref));

    /// <summary>取参考尺寸附着信息(<c>ProDimensionAttachmentsGet</c>——该 API 的合法主场
    /// 就是 reference/driven 尺寸,ProDimension.h 明写 "type must be PRO_REF_DIMENSION or
    /// PRO_DIMENSION")。不存在或不适用返 null。</summary>
    public DimensionAttachments? GetAttachments()
    {
        var raw = Session.Run(n => n.DimensionAttachmentsGet(Ref));
        CreoSdkLog.Trace("refdimension", "getattachments",
            new { model = Ref.Model.Name, id = Ref.Id, found = raw is not null });
        return raw is null ? null : CreoDimension.ToPublic(Session, raw);
    }
}
