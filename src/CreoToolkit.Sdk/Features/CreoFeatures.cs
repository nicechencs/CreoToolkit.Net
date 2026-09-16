using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>
/// 特征/元素树门面(session-bound)。元素树抽取走**非 static 入口**——
/// <see cref="ExtractElementTree"/> 经会话与 dispatcher, 不经 static 工厂，避免绕过生命周期管理。
/// </summary>
public sealed class CreoFeatures
{
    private readonly CreoSession _session;

    internal CreoFeatures(CreoSession session) => _session = session;

    /// <summary>取当前模型的首个可枚举特征; 无则 null。</summary>
    public CreoFeature? First()
    {
        var id = _session.Run(n => n.ModelCurrent());
        if (id is not { } v)
            return null;
        return First(v);
    }

    public CreoFeature? First(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        model.EnsureBelongsTo(_session);
        return First(model.Identity);
    }

    private CreoFeature? First(ModelIdentity model)
    {
        var fr = _session.Run(n => n.FirstFeature(model));
        return fr is { } f ? new CreoFeature(_session, f) : null;
    }

    /// <summary>列出模型所有可枚举特征; 非 solid 返回空列表。</summary>
    public IReadOnlyList<CreoFeature> List(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        model.EnsureBelongsTo(_session);
        var refs = _session.Run(n => n.FeatureList(model.Identity));
        var arr = new CreoFeature[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            arr[i] = new CreoFeature(_session, refs[i]);
        return arr;
    }

    /// <summary>
    /// 列出模型内 datum plane 特征(feat_type == <c>PRO_FEAT_DATUM</c>=923)。
    /// 内部经 <see cref="List"/> 全量枚举 + <see cref="CreoFeature.GetInfo"/> 逐条筛,
    /// 不新增 native 调用。<c>GetInfo()</c> 不可读的 feature 视为不匹配。
    /// </summary>
    public IReadOnlyList<CreoFeature> ListDatumPlaneFeatures(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        model.EnsureBelongsTo(_session);
        var all = List(model);
        if (all.Count == 0) return Array.Empty<CreoFeature>();
        var result = new List<CreoFeature>();
        foreach (var f in all)
        {
            if (f.GetInfo()?.FeatureType == DatumPlaneElementIds.PRO_FEAT_DATUM)
                result.Add(f);
        }
        return result;
    }

    /// <summary>删除特征(CLIP 模式, 连带依赖子特征一起删除)。删除后 feature 对象即失效(不可再用)。</summary>
    public void Delete(CreoFeature feature)
    {
        int fid = 0;
        CreoSdkLog.Run(
            "feature", "delete",
            body: () =>
            {
                ThrowUtil.IfNull(feature);
                feature.EnsureBelongsTo(_session);
                fid = feature.Id;
                _session.Run(n => n.FeatureDelete(feature.Ref));
            },
            failProps: () => new { id = fid });
    }

    /// <summary>提取特征元素树并写入 XML 文件(全生命周期内闭)。</summary>
    public void WriteElementTreeXml(CreoFeature feature, string xmlPath)
    {
        int fid = 0;
        CreoSdkLog.Run(
            "feature", "elemtree_write_xml",
            body: () =>
            {
                ThrowUtil.IfNull(feature);
                ThrowUtil.IfNullOrWhiteSpace(xmlPath);
                feature.EnsureBelongsTo(_session);
                fid = feature.Id;
                _session.Run(n => n.ElemtreeWriteXml(feature.Ref, xmlPath));
            },
            failProps: () => new { id = fid, path = xmlPath });
    }

    /// <summary>
    /// 深度抽取特征的 LIVE 元素树(带类型化元素值)。与 ExtractElementTree 同生命周期约定。
    /// </summary>
    public CreoRichElementTree ExtractElementTreeDeep(CreoFeature feature)
    {
        int fid = 0;
        int nodes = 0;
        return CreoSdkLog.Run(
            "feature", "elemtree_extract_deep",
            body: () =>
            {
                ThrowUtil.IfNull(feature);
                feature.EnsureBelongsTo(_session);
                fid = feature.Id;
                var result = _session.Run(n => n.ElemtreeExtractDeep(feature.Ref));
                result = new ElemtreeExtractDeepResult(_session.TrackResource(result.Tree), result.Nodes);
                nodes = result.Nodes.Count;
                return new CreoRichElementTree(_session, result);
            },
            failProps: () => new { id = fid },
            okProps: _ => new { id = fid, nodes });
    }

    /// <summary>
    /// 以 offset 约束创建基准面特征:参照平面 + 偏移距离。
    /// 内部组装 datum plane 元素树 spec(PRO_FEAT_DATUM + offset 约束,见 <see cref="DatumPlaneTree"/>),
    /// 经 Bridge 单次调用序列创建;返回新 <see cref="CreoFeature"/>。
    /// </summary>
    /// <param name="model">目标模型(须在会话中)。</param>
    /// <param name="referencePlane">偏移参照(平面 surface / 基准面,见 ProDtmPln.h Note 1)。</param>
    /// <param name="offset">偏移距离(模型长度单位,可为负,方向随参照法向)。</param>
    public CreoFeature CreateDatumPlane(CreoModel model, CreoModelItem referencePlane, double offset)
    {
        int fid = 0;
        return CreoSdkLog.Run(
            "feature", "create_datum_plane",
            body: () =>
            {
                ThrowUtil.IfNull(model);
                ThrowUtil.IfNull(referencePlane);
                model.EnsureBelongsTo(_session);
                referencePlane.EnsureBelongsTo(_session);
                var spec = DatumPlaneTree.BuildOffsetSpec(referencePlane.Ref, offset);
                var fr = _session.Run(n => n.FeatureCreateFromElemtree(model.Identity, spec));
                fid = fr.Id;
                return new CreoFeature(_session, fr);
            },
            failProps: () => new { refId = referencePlane?.Id, offset },
            okProps: _ => new { id = fid, refId = referencePlane!.Id, offset });
    }

    /// <summary>
    /// 修改孔特征直径(extract → 定位 PRO_E_HLE_COM/PRO_E_DIAMETER → 改值 → Redefine)。
    /// </summary>
    /// <param name="model">目标模型(须在会话中)。</param>
    /// <param name="holeFeature">孔特征(须属于目标模型)。</param>
    /// <param name="diameter">新直径(须为正数)。</param>
    public void RedefineHoleDiameter(CreoModel model, CreoFeature holeFeature, double diameter)
    {
        int fid = 0;
        CreoSdkLog.Run(
            "feature", "redefine_hole_diameter",
            body: () =>
            {
                ThrowUtil.IfNull(model);
                ThrowUtil.IfNull(holeFeature);
                if (diameter <= 0)
                    throw new ArgumentOutOfRangeException(nameof(diameter), diameter, "直径必须为正数");
                model.EnsureBelongsTo(_session);
                holeFeature.EnsureBelongsTo(_session);
                fid = holeFeature.Id;
                var elemIdPath = new[] { HoleElementIds.PRO_E_HLE_COM, HoleElementIds.PRO_E_DIAMETER };
                _session.Run(n => n.FeatureRedefineElemDouble(
                    model.Identity, holeFeature.Id, elemIdPath, diameter));
            },
            failProps: () => new { id = fid, diameter },
            okProps: () => new { id = fid, diameter });
    }

    /// <summary>
    /// 抽取特征的 LIVE 元素树。返回的 <see cref="CreoElementTree"/> 拥有该 LIVE 句柄, Dispose 时经
    /// dispatcher 用 **<c>ProFeatureElemtreeFree</c>(带 feature 上下文)** 释放——
    /// 绝非裸 ProElementFree（后者缺少 feature 上下文，会导致 Creo 内存状态错乱）。
    /// </summary>
    public CreoElementTree ExtractElementTree(CreoFeature feature)
    {
        int fid = 0;
        int nodes = 0;
        return CreoSdkLog.Run(
            "feature", "elemtree_extract",
            body: () =>
            {
                ThrowUtil.IfNull(feature);
                feature.EnsureBelongsTo(_session);
                fid = feature.Id;
                var result = _session.Run(n => n.ElemtreeExtract(feature.Ref));
                result = new ElemtreeExtractResult(_session.TrackResource(result.Tree), result.Nodes);
                nodes = result.Nodes.Count;
                return new CreoElementTree(_session, result);
            },
            failProps: () => new { id = fid },
            okProps: _ => new { id = fid, nodes });
    }
}

/// <summary>
/// 借用 ProFeature 的会话对象: **非 <see cref="IDisposable"/>**, 但 **session-bound**。
/// 只暴露值摘要(<see cref="Id"/>)与作用域内操作; 不导出任何 native token。
/// </summary>
public sealed class CreoFeature : CreoModelItem
{
    internal CreoFeature(CreoSession session, ItemRef itemRef)
        : base(session, itemRef) { }

    /// <summary>读取特征 status + type 信息快照；无效特征返 null。</summary>
    public CreoFeatureInfo? GetInfo()
        => Session.Run(n => n.FeatureInfoGet(Ref));

    /// <summary>读取父特征 id 列表；无父特征返空列表。</summary>
    public IReadOnlyList<int> GetParentIds()
        => Session.Run(n => n.FeatureParentsGet(Ref));

    /// <summary>读取父特征对象列表；无父特征返空列表。</summary>
    public IReadOnlyList<CreoFeature> GetParents()
        => IdsToFeatures(Session.Run(n => n.FeatureParentsGet(Ref)));

    /// <summary>读取子特征 id 列表；无子特征返空列表。</summary>
    public IReadOnlyList<int> GetChildIds()
        => Session.Run(n => n.FeatureChildrenGet(Ref));

    /// <summary>读取子特征对象列表；无子特征返空列表。</summary>
    public IReadOnlyList<CreoFeature> GetChildren()
        => IdsToFeatures(Session.Run(n => n.FeatureChildrenGet(Ref)));

    /// <summary>列出本特征内指定类型 geomitem(<c>ProFeatureGeomitemVisit</c>, DATA 快照)。
    /// datum point 等无 solid 级枚举 API 的类型经此收集;无该类项返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListGeomItems(CreoModelItemType itemType)
    {
        var refs = Session.Run(n => n.FeatureGeomitemsList(Ref, itemType));
        if (refs.Count == 0) return Array.Empty<ICreoModelItem>();
        var result = new ICreoModelItem[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = CreateFromRef(Session, refs[i]);
        return result;
    }

    private IReadOnlyList<CreoFeature> IdsToFeatures(IReadOnlyList<int> ids)
    {
        if (ids.Count == 0) return Array.Empty<CreoFeature>();
        var result = new CreoFeature[ids.Count];
        for (int i = 0; i < ids.Count; i++)
            result[i] = new CreoFeature(Session, new ItemRef(Ref.Model, CreoModelItemType.Feature, ids[i]));
        return result;
    }

    /// <summary>删除本特征(CLIP 模式)。删除后本对象即失效。</summary>
    public void Delete() => Session.Features.Delete(this);

    /// <summary>便捷形式: 抽取本特征的 LIVE 元素树。</summary>
    public CreoElementTree ExtractElementTree() => Session.Features.ExtractElementTree(this);

    /// <summary>便捷形式: 深度抽取本特征的 LIVE 元素树(带元素值)。</summary>
    public CreoRichElementTree ExtractElementTreeDeep() => Session.Features.ExtractElementTreeDeep(this);
}

/// <summary>
/// owned LIVE 元素树(LIVE 三态的 L3 表现)。<see cref="IDisposable"/>: Dispose 时经 dispatcher 用
/// 带 feature 上下文的 elemtree free 释放 owned 句柄。第一阶段只读: <see cref="Walk"/> 返回抽取时即
/// copy-out 的节点值摘要(<see cref="CreoElementNode"/> 是纯值, 释放后仍可持有), 而 Walk 本身在树已
/// Dispose 后调用即抛 <see cref="ObjectDisposedException"/>(父作用域绑定)。
/// </summary>
public sealed class CreoElementTree : IDisposable
{
    private readonly CreoSession _session;
    private readonly INativeResource _tree;
    private readonly IReadOnlyList<CreoElementNode> _nodes;
    private int _disposed; // 0=活, 1=已释放; 原子翻转保证 Dispose 幂等

    internal CreoElementTree(CreoSession session, ElemtreeExtractResult result)
    {
        _session = session;
        _tree = result.Tree;
        _nodes = result.Nodes;
    }

    /// <summary>只读遍历元素树(返回抽取时的节点值快照)。树已释放则抛 <see cref="ObjectDisposedException"/>。</summary>
    public IEnumerable<CreoElementNode> Walk()
    {
        EnsureUsable();
        return _nodes;
    }

    private void EnsureUsable()
    {
        ThrowUtil.IfDisposed(Volatile.Read(ref _disposed) != 0, this);
        _session.EnsureOpen();
    }

    /// <summary>释放 owned LIVE 树句柄(幂等; 经 dispatcher 在主线程; 带 feature 上下文)。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _session.Dispatcher.Invoke(() => _tree.Dispose());
    }
}
