using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 尺寸(DHandle 衍生;session-bound)。操作留 future 按业务排队加。
/// </summary>
public sealed class CreoDimension : CreoModelItem
{
    internal CreoDimension(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>尺寸值（ProDimensionValueGet）；不可读返 null。</summary>
    public double? GetValue() => Session.Run(n => n.DimensionValueGet(Ref));

    /// <summary>尺寸类型 int（pro_dimensiontype 枚举）；不可读返 null。</summary>
    public int? GetTypeCode() => Session.Run(n => n.DimensionTypeGet(Ref));

    /// <summary>尺寸显示文本，含前缀/值/后缀/公差（ProDimensionTextWstringsGet）；不可读返 null。</summary>
    public string? GetDisplayText() => Session.Run(n => n.DimensionTextGet(Ref));

    /// <summary>取尺寸附着信息(<c>ProDimensionAttachmentsGet</c>):标注平面 + 附着 pair 数 +
    /// 每 pair 的 sense。<br/>
    /// <b>适用范围</b>(ProDimension.h):该 native API 只对 reference/driven 尺寸有效;
    /// driving(普通)尺寸不适用,统一返 null(INVALID_ITEM/BAD_INPUTS 已在 Bridge 归 null,
    /// 不抛异常)。参考尺寸走 <see cref="CreoRefDimension.GetAttachments"/>。<br/>
    /// AnnotationPlane 反查成 wrapper(常见 <see cref="CreoSurface"/> / <see cref="CreoCsys"/>);
    /// pair 内 raw ProSelection handle 不外露(生命周期归专用嵌套 free),需要 handle 请后续加
    /// ProSelectionCopy 副本机制。</summary>
    public DimensionAttachments? GetAttachments()
    {
        var raw = Session.Run(n => n.DimensionAttachmentsGet(Ref));
        CreoSdkLog.Trace("dimension", "getattachments",
            new { model = Ref.Model.Name, id = Ref.Id, found = raw is not null });
        return raw is null ? null : ToPublic(Session, raw);
    }

    internal static DimensionAttachments ToPublic(CreoSession session, DimensionAttachmentsInfo raw)
    {
        ICreoModelItem? plane = raw.AnnotationPlane is { } planeRef
            ? CreoModelItem.CreateFromRef(session, planeRef)
            : null;
        var senses = raw.Senses.Count == 0
            ? Array.Empty<DimSense>()
            : raw.Senses.Select(s => new DimSense(s.Type, s.Sense, s.OrientHint)).ToArray();
        CreoVec3? location = raw.LocationXyz is { } xyz
            ? new CreoVec3(xyz.X, xyz.Y, xyz.Z)
            : null;
        return new DimensionAttachments(plane, raw.OrientHint, raw.AttachmentCount, senses, location);
    }
}
