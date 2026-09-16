using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>统一模型项查询 facade(挂在 <see cref="CreoModel.Items"/>)。
/// 按 <see cref="CreoModelItemType"/> 路由到各 visitor/facade,不移动 Parameters。</summary>
public sealed class CreoModelItems
{
    private readonly CreoSession _session;
    private readonly CreoModel _model;

    internal CreoModelItems(CreoSession session, CreoModel model)
    {
        _session = session;
        _model = model;
    }

    /// <summary>列出指定类型的模型项。不支持的类型抛 <see cref="NotSupportedException"/>。</summary>
    public IReadOnlyList<ICreoModelItem> List(CreoModelItemType type)
    {
        _session.EnsureOpen();
        return type switch
        {
            CreoModelItemType.Feature => ListFeatures(),
            CreoModelItemType.Axis => ListFromSolid(n => n.SolidAxisList(_model.Identity)),
            CreoModelItemType.Csys => ListFromSolid(n => n.SolidCsysList(_model.Identity)),
            CreoModelItemType.Surface => ListFromSolid(n => n.SolidSurfaceList(_model.Identity)),
            CreoModelItemType.Quilt => ListFromSolid(n => n.SolidQuiltList(_model.Identity)),
            CreoModelItemType.Dimension => ListFromSolid(n => n.SolidDimensionList(_model.Identity, false)),
            CreoModelItemType.RefDimension => ListRefDimensionsOnly(),
            CreoModelItemType.Layer => ListLayers(),
            CreoModelItemType.Curve => ListFromSolid(n => n.SolidCurveList(_model.Identity)),
            _ => throw new NotSupportedException(
                $"CreoModelItems.List 不支持类型 {type}。支持: Feature/Axis/Csys/Surface/Quilt/Dimension/RefDimension/Layer/Curve。"),
        };
    }

    /// <summary>按 id 查找指定类型的模型项；未找到返 null。</summary>
    public ICreoModelItem? GetById(CreoModelItemType type, int id)
    {
        var items = List(type);
        foreach (var item in items)
        {
            if (item is CreoModelItem mi && mi.Ref.Id == id)
                return item;
        }
        return null;
    }

    /// <summary>按名字查找指定类型的模型项；未找到返 null。
    /// 内部对每项调用 <see cref="ICreoModelItem.GetName"/> 匹配。</summary>
    public ICreoModelItem? GetByName(CreoModelItemType type, string name)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        var items = List(type);
        foreach (var item in items)
        {
            if (string.Equals(item.GetName(), name, StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    private IReadOnlyList<ICreoModelItem> ListFeatures()
    {
        if (_model is not CreoSolid)
            return Array.Empty<ICreoModelItem>();
        var features = _session.Features.List(_model);
        var result = new ICreoModelItem[features.Count];
        for (int i = 0; i < features.Count; i++)
            result[i] = features[i];
        return result;
    }

    private IReadOnlyList<ICreoModelItem> ListRefDimensionsOnly()
    {
        if (_model is not CreoSolid)
            return Array.Empty<ICreoModelItem>();
        var refs = _session.Run(n => n.SolidDimensionList(_model.Identity, true));
        var filtered = new List<ItemRef>();
        foreach (var r in refs)
        {
            if (r.Type == CreoModelItemType.RefDimension)
                filtered.Add(r);
        }
        return CreoModel.Materialize(_session, filtered);
    }

    /// <summary>Layer 走 model-level(不限 Solid,drawing/notebook 也可能有 layer)。</summary>
    private IReadOnlyList<ICreoModelItem> ListLayers()
    {
        var refs = _session.Run(n => n.ModelLayerList(_model.Identity));
        return CreoModel.Materialize(_session, refs);
    }

    private IReadOnlyList<ICreoModelItem> ListFromSolid(Func<ICreoNative, IReadOnlyList<ItemRef>> accessor)
    {
        if (_model is not CreoSolid)
            return Array.Empty<ICreoModelItem>();
        var refs = _session.Run(accessor);
        return CreoModel.Materialize(_session, refs);
    }
}
