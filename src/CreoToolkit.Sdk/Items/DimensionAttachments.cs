namespace CreoToolkit.Sdk;

/// <summary>
/// 尺寸/GTol 附着信息(3D 尺寸 / 图纸尺寸 / GTol make-dim 共用返回结构)。
/// <para>
/// <b>AnnotationPlane</b>: 标注平面 wrapper(通常是 <see cref="CreoSurface"/> 或 <see cref="CreoCsys"/>);
/// 图纸尺寸不含此字段返 null。<br/>
/// <b>OrientHint</b>: <c>pro_dim_orient</c> 枚举 int(方向偏好);未定义返 0。<br/>
/// <b>AttachmentCount</b>: attach pair 数(每 pair = ProSelection[2] intersection type);
/// pair 内 handle 未外露(生命周期归 <c>ProDimattachmentarrayFree</c>,不能长期持有)。<br/>
/// <b>Senses</b>: 每 pair 对应一条 <see cref="DimSense"/>(type/sense/orient_hint 三主字段)。<br/>
/// <b>Location</b>: 仅 <c>GtolAttachMakeDimGet</c> 填充,尺寸/图纸尺寸返 null。
/// </para>
/// </summary>
public sealed record DimensionAttachments(
    ICreoModelItem? AnnotationPlane,
    int OrientHint,
    int AttachmentCount,
    IReadOnlyList<DimSense> Senses,
    CreoVec3? Location);

/// <summary>Attachment pair 对应的 sense(type/sense/orient_hint 三主字段)。
/// angle_sense 12B 子结构暂未拆包(需要时再补)。</summary>
public sealed record DimSense(int Type, int Sense, int OrientHint);
