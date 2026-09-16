using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>模型 layer 门面。只暴露值语义操作，不暴露 ProLayer 句柄。</summary>
public sealed class CreoLayers
{
    private readonly CreoSession _session;
    private readonly ModelIdentity _model;

    internal CreoLayers(CreoSession session, ModelIdentity model)
    {
        _session = session;
        _model = model;
    }

    /// <summary>列出模型中的 layer 名称快照。</summary>
    public IReadOnlyList<string> ListNames()
        => _session.Run(n => n.LayerNamesList(_model));

    /// <summary>列出模型中的 layer(每个包 CreoLayer wrapper 供 <see cref="CreoLayer.ListItems"/> 等 OHandle 操作)。
    /// 无 layer 或不可读返空列表。等价 <c>CreoModelItems.List(Layer)</c>,提供 layer-oriented 便捷入口。</summary>
    public IReadOnlyList<CreoLayer> List()
    {
        var refs = _session.Run(n => n.ModelLayerList(_model));
        var list = new CreoLayer[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            list[i] = new CreoLayer(_session, refs[i]);
        return list;
    }

    /// <summary>创建 layer；若已存在则返回 false。</summary>
    public bool Create(string name) =>
        CreoSdkLog.Run(
            "layer", "create",
            body: () =>
            {
                ValidateName(name);
                return _session.Run(n => n.LayerCreate(_model, name));
            },
            failProps: () => new { name },
            okProps: created => new { name, created });

    /// <summary>删除 layer；若不存在则返回 false。</summary>
    public bool Delete(string name) =>
        CreoSdkLog.Run(
            "layer", "delete",
            body: () =>
            {
                ValidateName(name);
                return _session.Run(n => n.LayerDelete(_model, name));
            },
            failProps: () => new { name },
            okProps: deleted => new { name, deleted });

    /// <summary>读取 layer 的临时显示状态；layer 不存在时返回 null。</summary>
    public CreoLayerDisplayStatus? GetDisplayStatus(string name)
    {
        ValidateName(name);
        return _session.Run(n => n.LayerDisplayStatusGet(_model, name));
    }

    /// <summary>设置 layer 的临时显示状态；layer 不存在时返回 false。</summary>
    public bool SetDisplayStatus(string name, CreoLayerDisplayStatus status) =>
        CreoSdkLog.Run(
            "layer", "setdisplaystatus",
            body: () =>
            {
                ValidateName(name);
                ValidateDisplayStatus(status);
                return _session.Run(n => n.LayerDisplayStatusSet(_model, name, status));
            },
            failProps: () => new { name, status = status.ToString() },
            okProps: applied => new { name, status = status.ToString(), applied });

    /// <summary>把同一模型中的 feature 加入 layer；layer 不存在或已包含时返回 false。</summary>
    public bool AddFeature(string name, CreoFeature feature)
    {
        int featureId = 0;
        return CreoSdkLog.Run(
            "layer", "addfeature",
            body: () =>
            {
                ValidateName(name);
                ValidateFeature(feature);
                featureId = feature.Ref.Id;
                return _session.Run(n => n.LayerFeatureAdd(_model, name, feature.Ref));
            },
            failProps: () => new { name, featureId },
            okProps: added => new { name, featureId, added });
    }

    /// <summary>查询 layer 是否包含同一模型中的 feature；layer 不存在时返回 false。</summary>
    public bool ContainsFeature(string name, CreoFeature feature)
    {
        ValidateName(name);
        ValidateFeature(feature);
        return _session.Run(n => n.LayerFeatureContains(_model, name, feature.Ref));
    }

    private static void ValidateName(string name)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        if (name.Length >= CtkAbiConstants.ProNameSize)
            throw new ArgumentOutOfRangeException(nameof(name), "Layer 名称不能超过 ProName 容量。");
    }

    private static void ValidateDisplayStatus(CreoLayerDisplayStatus status)
    {
        switch (status)
        {
            case CreoLayerDisplayStatus.None:
            case CreoLayerDisplayStatus.Normal:
            case CreoLayerDisplayStatus.Display:
            case CreoLayerDisplayStatus.Blank:
            case CreoLayerDisplayStatus.Hidden:
            case CreoLayerDisplayStatus.Skip:
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "未知 layer 显示状态。");
        }
    }

    private void ValidateFeature(CreoFeature feature)
    {
        ThrowUtil.IfNull(feature);
        feature.EnsureBelongsTo(_session);
        if (feature.Ref.Model != _model)
            throw new InvalidOperationException("CreoFeature 不属于该 layer 所在模型。");
    }
}
