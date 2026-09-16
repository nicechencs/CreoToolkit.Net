using System.Collections;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>选择门面(对齐 OTK <c>pfcBaseSession::Select</c> 与 <c>pfcSelect</c> 全局工厂;
/// 复数命名对齐 <see cref="CreoModels"/>/<see cref="CreoLayers"/>/<see cref="CreoFeatures"/>)。session-bound。</summary>
public sealed class CreoSelections
{
    private readonly CreoSession _session;

    internal CreoSelections(CreoSession session) => _session = session;

    /// <summary>[Obsolete] 兼容入口。仅空列表创建模型自身 selection；非空字符串引用
    /// 没有已定义的解析语法并明确拒绝。使用
    /// <see cref="Select(string, int)"/>/<see cref="CreateModelItemSelection"/> 替代。</summary>
    [Obsolete("已废弃：改用 CreateModelSelection(model)、Select(optionKeywords) 或 CreateModelItemSelection(item,path)；不支持非空字符串 itemRefs。")]
    public CreoSelectionSet PickProgrammatic(IReadOnlyList<string> itemRefs)
    {
        ThrowUtil.IfNull(itemRefs);
        if (itemRefs.Count > 0)
            throw new NotSupportedException("PickProgrammatic 未定义字符串 itemRefs 解析语法；已解析项请用 CreateModelItemSelection，模型自身请用 CreateModelSelection，交互点选请用 Select。");
        var id = _session.Run(n => n.ModelCurrent())
                 ?? throw new CreoException("ProSelect", ProError.BadContext, "无当前模型, 无法选择。");
        return PickProgrammaticCore(id, itemRefs);
    }

    [Obsolete("已废弃：改用 CreateModelSelection(model)、Select(optionKeywords) 或 CreateModelItemSelection(item,path)。")]
    public CreoSelectionSet PickProgrammatic(CreoModel model, IReadOnlyList<string> itemRefs)
    {
        ThrowUtil.IfNull(model);
        model.EnsureBelongsTo(_session);
        ThrowUtil.IfNull(itemRefs);
        if (itemRefs.Count > 0)
            throw new NotSupportedException("PickProgrammatic 未定义字符串 itemRefs 解析语法；已解析项请用 CreateModelItemSelection，模型自身请用 CreateModelSelection，交互点选请用 Select。");
        return PickProgrammaticCore(model.Identity, itemRefs);
    }

    /// <summary>创建代表 <paramref name="model"/> 自身的 owned selection——
    /// 空 <c>PickProgrammatic</c> 兼容调用的显式替代。用完 Dispose 释放 native 副本。</summary>
    public CreoSelectionSet CreateModelSelection(CreoModel model)
    {
        ThrowUtil.IfNull(model);
        model.EnsureBelongsTo(_session);
        return PickProgrammaticCore(model.Identity, Array.Empty<string>());
    }

    #pragma warning disable CS0618 // 内部调用 obsolete 接缝,支撑 tests 过渡
    private CreoSelectionSet PickProgrammaticCore(ModelIdentity model, IReadOnlyList<string> itemRefs)
        => CreoSdkLog.Run(
            "selection", "pick-programmatic",
            body: () =>
            {
                var result = _session.Run(n => n.SelectProgrammatic(model, itemRefs));
                result = new SelectResult(_session.TrackResource(result.Set), result.Items);
                return new CreoSelectionSet(_session, model, result);
            },
            failProps: () => new { model = model.Name, type = model.Type.ToString(), count = itemRefs.Count },
            okProps: set => new { model = model.Name, type = model.Type.ToString(), count = itemRefs.Count, selected = set.Count });
    #pragma warning restore CS0618

    /// <summary>交互式选择(对齐 <c>pfcBaseSession::Select</c>):弹 Creo UI 让用户点选
    /// (阻塞主线程,middle-click 结束返回)。
    /// <para>返回 **owned 副本集**(bridge 内逐条 ProSelectionCopy):用 <c>using</c> 释放;
    /// 副本集存活期内各项可 <see cref="CreoSelection.Highlight"/>/<see cref="CreoSelection.Display"/>。
    /// 用户取消或零选中返回空集(Count=0,仍须释放)。</para>
    /// <para>每项带 per-item owner 与装配路径(交互选择可跨模型;owner 不可解析时
    /// <see cref="CreoSelection.SelItem"/>=null 降级,raw 信息仍在
    /// <see cref="CreoSelection.RawTypeCode"/>/<see cref="CreoSelection.Id"/>)。</para>
    /// <para><b>真机验证边界</b>:owned 副本支撑 Highlight/Display 已由 programmatic 路径真机
    /// smoke 佐证;copy 对交互 selection 的装配路径/view 上下文完整性、跨窗口切换后的有效性
    /// 未真机验证——契约保守为"副本集未释放 + session 开放 + owner/窗口上下文未变"。</para></summary>
    public CreoSelectionSet Select(CreoSelectionOptions options)
    {
        ThrowUtil.IfNull(options);
        return SelectCore(options.OptionKeywords, options.MaxNumSels);
    }

    /// <summary>便捷重载(等价 <c>Select(new CreoSelectionOptions(optionKeywords){MaxNumSels=...})</c>)。
    /// <para><paramref name="optionKeywords"/> 必填:tkuse/otkug 官方 ProSelect filter token(如
    /// <see cref="CreoSelectFilters.DraftEntity"/>),多值英文逗号分隔
    /// (<see cref="CreoSelectFilters.Join"/>)。注意"any"不是有效 token——drawing context
    /// 真机实证任何 item 都选不中(2026-07-07),必须按上下文传定向 token。</para>
    /// <para>maxNumSels 默认 <c>-1</c>=不限数量(官方默认,pfcSelectionOptions/pt_examples
    /// TestDimension.c:1319 双证);0 与 &lt;-1 非法抛 <see cref="ArgumentOutOfRangeException"/>。</para></summary>
    public CreoSelectionSet Select(string optionKeywords, int maxNumSels = -1)
    {
        ThrowUtil.IfNullOrEmpty(optionKeywords);
        if (maxNumSels == 0 || maxNumSels < -1)
            throw new ArgumentOutOfRangeException(nameof(maxNumSels), maxNumSels, "maxNumSels 必须 >0 或 -1(不限数量)");
        return SelectCore(optionKeywords, maxNumSels);
    }

    private CreoSelectionSet SelectCore(string optionKeywords, int maxNumSels)
        => CreoSdkLog.Run(
            "selection", "select",
            body: () =>
            {
                var result = _session.Run(n => n.Select(optionKeywords, maxNumSels));
                result = new SelectResult(_session.TrackResource(result.Set), result.Items);
                return new CreoSelectionSet(_session, ownerModel: null, result);
            },
            failProps: () => new { optionKeywords, maxNumSels },
            okProps: set => new { optionKeywords, maxNumSels, count = set.Count });

    /// <summary>由 (item, path) 创建 selection(对齐 <c>pfcSelect</c> 全局工厂
    /// <c>CreateModelItemSelection</c>)——ProSelection 与 (modelitem + asmcomppath) 双向可逆
    /// (ProSelection.h:ModelitemGet/AsmcomppathGet 抽取,Alloc(path,item) 重建)。
    /// <para><paramref name="path"/>=null 仅限 part/顶层场景(头文件组合表:assembly 内组件几何
    /// 必须给 path,否则 native BAD_INPUTS)。path 可直接取自
    /// <see cref="CreoSelection.Path"/>(交互选择抽出的路径)。</para>
    /// <para>返回单项 owned 副本集(用 <c>using</c> 释放),可 Highlight/Display。
    /// item/path root 模型不在会话抛 <see cref="CreoException"/>;stale id(装配结构已变)
    /// 由 native 报错兜底。</para></summary>
    public CreoSelectionSet CreateModelItemSelection(CreoModelItem item, CreoComponentPath? path = null)
    {
        ThrowUtil.IfNull(item);
        item.EnsureBelongsTo(_session);
        path?.EnsureBelongsTo(_session);
        if (path is not null)
        {
            var leaf = path.GetLeaf();
            if (leaf is not null && leaf.Identity != item.Ref.Model)
                throw new ArgumentException(
                    $"selection item owner '{item.Ref.Model.Name}' 与 component path leaf '{leaf.Identity.Name}' 不匹配。",
                    nameof(path));
        }
        ComponentPathData? pathData = path is null
            ? null
            : new ComponentPathData(path.Root.Identity, path.ComponentIds);
        return CreoSdkLog.Run(
            "selection", "create-modelitem-selection",
            body: () =>
            {
                var result = _session.Run(n => n.CreateModelItemSelection(item.Ref, pathData));
                result = new SelectResult(_session.TrackResource(result.Set), result.Items);
                return new CreoSelectionSet(_session, ownerModel: null, result);
            },
            failProps: () => new { model = item.Ref.Model.Name, type = item.Type.ToString(), id = item.Id, hasPath = path != null },
            okProps: set => new { model = item.Ref.Model.Name, type = item.Type.ToString(), id = item.Id, hasPath = path != null, count = set.Count });
    }
}

/// <summary>选择选项(对齐 <c>pfcSelectionOptions</c>):<see cref="OptionKeywords"/>=filter token
/// 逗号串;<see cref="MaxNumSels"/> 默认 <c>-1</c>=不限数量(官方默认)。</summary>
public sealed class CreoSelectionOptions
{
    private readonly int _maxNumSels = -1;

    public CreoSelectionOptions(string optionKeywords)
    {
        ThrowUtil.IfNullOrEmpty(optionKeywords);
        OptionKeywords = optionKeywords;
    }

    /// <summary>选择过滤 token(逗号分隔,见 <see cref="CreoSelectFilters"/>)。</summary>
    public string OptionKeywords { get; }

    /// <summary>选择数上限;<c>-1</c>=不限(官方默认)。0 与 &lt;-1 非法。</summary>
    public int MaxNumSels
    {
        get => _maxNumSels;
        init
        {
            if (value == 0 || value < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "MaxNumSels 必须 >0 或 -1(不限数量)");
            _maxNumSels = value;
        }
    }
}

/// <summary>ProSelect filter token 常量(tkuse §pro_select option 官方表逐值核对,2026-07-07)。
/// <para>多值经 <see cref="Join"/> 逗号拼接。drawing context 必须传定向 token
/// ("any" 非法,真机选不中任何 item)。</para></summary>
public static class CreoSelectFilters
{
    // ── Geometry Items(几何项) ──

    /// <summary>基准点 → PRO_POINT。</summary>
    public const string Point = "point";

    /// <summary>基准轴 → PRO_AXIS。</summary>
    public const string Axis = "axis";

    /// <summary>基准面 → PRO_SURFACE。</summary>
    public const string DatumPlane = "datum";

    /// <summary>坐标系基准 → PRO_CSYS。</summary>
    public const string Csys = "csys";

    /// <summary>坐标系轴 → PRO_CSYS_AXIS_X / PRO_CSYS_AXIS_Y / PRO_CSYS_AXIS_Z。</summary>
    public const string CsysAxis = "csys_axis";

    /// <summary>边(实体或基准面) → PRO_EDGE。drawing view 里点击 part 边线投影即走此
    /// token(官方 tkuse ProSelect 表:Geometry Items 分组,非 Drawing Items 分组——
    /// 标注类 token 只覆盖 draft_ent/note/dim 等,不含几何投影)。</summary>
    public const string Edge = "edge";

    /// <summary>顶点 → PRO_EDGE_START 或 PRO_EDGE_END。</summary>
    public const string EdgeEnd = "edge_end";

    /// <summary>基准曲线 → PRO_CURVE。</summary>
    public const string Curve = "curve";

    /// <summary>基准曲线端点 → PRO_CRV_START 或 PRO_CRV_END。</summary>
    public const string CurveEnd = "curve_end";

    /// <summary>复合曲线 → PRO_CURVE。</summary>
    public const string CompositeCurve = "comp_crv";

    /// <summary>边(仅实体) → PRO_EDGE。</summary>
    public const string SolidEdge = "sldedge";

    /// <summary>边(仅基准面) → PRO_EDGE。</summary>
    public const string QuiltEdge = "qltedge";

    /// <summary>管道线段端点 → PRO_PSEG_START 或 PRO_PSEG_END。</summary>
    public const string PipeSegmentEnd = "pipeseg_end";

    /// <summary>面(实体或 quilt) → PRO_SURFACE。</summary>
    public const string Surface = "surface";

    /// <summary>面(仅实体) → PRO_SURFACE。</summary>
    public const string SolidFace = "sldface";

    /// <summary>面(仅基准面) → PRO_SURFACE。</summary>
    public const string QuiltFace = "qltface";

    /// <summary>面(点) → PRO_SURFACE_PNT。</summary>
    public const string PointSurface = "pntsrf";

    /// <summary>面组(quilt) → PRO_QUILT。</summary>
    public const string Quilt = "dtmqlt";

    // ── Annotations(标注) ──

    /// <summary>尺寸 → PRO_DIMENSION。</summary>
    public const string Dimension = "dimension";

    /// <summary>参考尺寸 → PRO_REF_DIMENSION。</summary>
    public const string RefDimension = "ref_dim";

    /// <summary>几何公差 → PRO_GTOL。</summary>
    public const string Gtol = "gtol";

    /// <summary>3D 符号 → PRO_SYMBOL_INSTANCE。</summary>
    public const string Symbol3d = "symbol_3d";

    /// <summary>注释 → PRO_NOTE(drawing detail note 与 3D note 官方共用本 token;
    /// 仅选 3D note 用官方 token "note_3d")。</summary>
    public const string Note = "any_note";

    /// <summary>3D 注释 → PRO_NOTE。</summary>
    public const string Note3d = "note_3d";

    /// <summary>3D 表面粗糙度 → PRO_SURF_FIN。</summary>
    public const string SurfaceFinish3d = "surffin_3d";

    /// <summary>标注元素 → PRO_ANNOTATION_ELEM。</summary>
    public const string AnnotationElement = "annot_elem";

    // ── Drawing Items(图纸项) ──

    /// <summary>工程图视图 → PRO_VIEW。</summary>
    public const string DrawingView = "dwg_view";

    /// <summary>图纸表格 → PRO_DRAW_TABLE。</summary>
    public const string DrawingTable = "dwg_table";

    /// <summary>draft 几何实体 → PRO_DRAFT_ENTITY(Sketch tab 手画圆/圆弧/椭圆等)。</summary>
    public const string DraftEntity = "draft_ent";

    /// <summary>detail 符号实例 → PRO_SYMBOL_INSTANCE。</summary>
    public const string DetailSymbol = "dtl_symbol";

    /// <summary>表格单元格 → PRO_DRAW_TABLE。</summary>
    public const string TableCell = "table_cell";

    // ── Solids and Features(实体与特征) ──

    /// <summary>特征 → PRO_FEATURE。</summary>
    public const string Feature = "feature";

    /// <summary>零件 → PRO_PART。</summary>
    public const string Part = "part";

    /// <summary>组件特征 → PRO_FEATURE。</summary>
    public const string ComponentFeature = "membfeat";

    /// <summary>装配组件模型 → PRO_PART 或 PRO_ASSEMBLY。</summary>
    public const string Component = "component";

    /// <summary>零件或子装配 → PRO_PART 或 PRO_ASSEMBLY。</summary>
    public const string PartOrAssembly = "prt_or_asm";

    // ── Miscellaneous Items(其他项) ──

    /// <summary>外部对象 → PRO_EXTOBJ。</summary>
    public const string ExternalObject = "ext_obj";

    /// <summary>示意图固定连接件/固定元件/参数化连接件 → PRO_DIAGRAM_OBJECT。</summary>
    public const string DiagramObject = "dgm_obj";

    /// <summary>示意图导线(非电缆) → PRO_DIAGRAM_OBJECT。</summary>
    public const string DiagramNonCableWire = "dgm_non_cable_wire";

    /// <summary>多 token 逗号拼接(ProSelect option 多值语法)。</summary>
    public static string Join(params string[] tokens) => string.Join(",", tokens);
}

/// <summary>
/// owned 选择副本集(BORROWED 三态的 L3 表现)。语义是"**拥有副本**", 用户看不到任何"借用句柄"
/// ——原 static borrow 是 L1 内部事实。<see cref="IDisposable"/>: Dispose 时经 dispatcher
/// 在主线程释放副本集。其下 <see cref="CreoSelection"/> **父作用域绑定**, 本集 Dispose 后访问即抛
/// <see cref="ObjectDisposedException"/>。
/// </summary>
public sealed class CreoSelectionSet : IReadOnlyList<CreoSelection>, IDisposable
{
    private readonly CreoSession _session;
    private readonly INativeResource _owned;
    private readonly CreoSelection[] _items;
    private int _disposed; // 0=活, 1=已释放; 原子翻转保证 Dispose 幂等

    internal CreoSelectionSet(CreoSession session, ModelIdentity? ownerModel, SelectResult result)
    {
        _session = session;
        _owned = result.Set;
        _items = new CreoSelection[result.Items.Count];
        for (var i = 0; i < _items.Length; i++)
        {
            var data = result.Items[i];
            // index 对齐硬校验:Highlight(index) 直达 owned 句柄数组,
            // bridge 的 data.Index 与集合位置错位即高亮错项——契约在此锁死
            if (data.Index != i)
                throw new InvalidOperationException(
                    $"selection index 错位: data.Index={data.Index}, 集合位置={i}(bridge 契约破坏)");
            // per-item owner 优先(交互选择可跨模型),无则回退集合级 owner
            var owner = data.Owner ?? ownerModel;
            var item = MaterializeItem(session, owner, data);
            var selModel = owner is { } o ? CreoModels.CreateModel(session, o) : null;
            var path = MaterializePath(session, data.Path);
            _items[i] = new CreoSelection(this, i, data, item, selModel, path);
        }
    }

    private static ICreoModelItem? MaterializeItem(CreoSession session, ModelIdentity? owner, SelectionItemData data)
    {
        // 双 null(交互 owner 不可解析)→ null 降级,raw 信息仍经 CreoSelection 属性可读
        if (owner is not { } o) return null;
        var itemRef = new ItemRef(o, data.ItemType, data.ItemId);
        // 委托共享工厂；未识别类型工厂返基类降级实例，selection 语义为 null（不暴露无意义基类）
        var item = CreoModelItem.CreateFromRef(session, itemRef);
        return item.GetType() == typeof(CreoModelItem) ? null : item;
    }

    private static CreoComponentPath? MaterializePath(CreoSession session, ComponentPathData? pathData)
    {
        if (pathData is not { } d) return null;
        // path root 反查为 CreoAssembly(交互抽取的 owner 已在会话内);非 assembly/失败返 null
        if (CreoModels.CreateModel(session, d.Root) is not CreoAssembly asm) return null;
        return new CreoComponentPath(session, asm, d.ComponentIds.ToArray());
    }

    /// <summary>副本数。</summary>
    public int Count
    {
        get { EnsureUsable(); return _items.Length; }
    }

    /// <summary>第 <paramref name="index"/> 个副本项。</summary>
    public CreoSelection this[int index]
    {
        get { EnsureUsable(); return _items[index]; }
    }

    /// <inheritdoc />
    public IEnumerator<CreoSelection> GetEnumerator()
    {
        EnsureUsable();
        return ((IEnumerable<CreoSelection>)_items).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // 父作用域绑定 + session 开放 双校验; 子项动作经此。
    internal void EnsureUsable()
    {
        ThrowUtil.IfDisposed(Volatile.Read(ref _disposed) != 0, this);
        _session.EnsureOpen();
    }

    // 子项高亮: 在副本集仍存活时, 经 dispatcher 作用于 owned 副本。
    internal void HighlightItem(int index)
        => _session.Run(n => n.SelectionHighlight(_owned, index));

    // 子项取消高亮: 与高亮共享同一 owned 副本作用域。
    internal void UnhighlightItem(int index)
        => _session.Run(n => n.SelectionUnhighlight(_owned, index));

    // 子项显示: 与高亮共享同一 owned 副本作用域。
    internal void DisplayItem(int index)
        => _session.Run(n => n.SelectionDisplay(_owned, index));

    /// <summary>释放 owned 副本集(幂等; 经 dispatcher 在主线程; 不走 open 检查, 保证 teardown 也能释放)。</summary>
    public void Dispose()
    {
        var alreadyDisposed = false;

        CreoSdkLog.Run(
            "selection", "release",
            body: () =>
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    alreadyDisposed = true;
                    return;
                }

                _session.Dispatcher.Invoke(() => _owned.Dispose());
            },
            failProps: () => new { type = "set", count = _items.Length },
            okProps: () => new { type = "set", count = _items.Length, alreadyDisposed });
    }
}

/// <summary>选择副本集中的一项(对齐 OTK <c>pfcSelection</c>/VB <c>IpfcSelection</c>)。
/// **父(Set)作用域绑定**:<see cref="Label"/>/<see cref="SelItem"/>/<see cref="SelModel"/>/
/// <see cref="Path"/> 是 copy-out 的 DATA 摘要(随取随读),而 <see cref="Highlight"/> 触达 native,
/// 父集 Dispose 后调用即抛 <see cref="ObjectDisposedException"/>。
/// <para><see cref="SelItem"/> 是强类型 <see cref="ICreoModelItem"/>;工厂已识别类型兑现,
/// 未识别 type 或 owner 不可解析落 null(降级不丢 raw 信息:<see cref="RawTypeCode"/>/<see cref="Id"/>)。</para></summary>
public sealed class CreoSelection
{
    private readonly CreoSelectionSet _parent;
    private readonly int _index;

    internal CreoSelection(CreoSelectionSet parent, int index, SelectionItemData data, ICreoModelItem? item, CreoModel? selModel, CreoComponentPath? path)
    {
        _parent = parent;
        _index = index;
        Label = data.Label;
        SelItem = item;
        SelModel = selModel;
        Id = data.ItemId;
        RawTypeCode = data.RawTypeCode;
        TypeName = data.RawTypeCode != 0
            ? (Enum.IsDefined(typeof(CreoModelItemType), data.RawTypeCode)
                ? ((CreoModelItemType)data.RawTypeCode).ToString()
                : $"Unknown({data.RawTypeCode})")
            : data.ItemType.ToString();
        Path = path;
    }

    /// <summary>项的显示标签(DATA 摘要;历史兼容,业务首选 <see cref="SelItem"/>)。</summary>
    public string Label { get; }

    /// <summary>选中的模型项(对齐 <c>pfcSelection::GetSelItem</c>)。工厂已识别类型兑现;
    /// 未识别 type 或 owner 不可解析落 <c>null</c>(降级不丢 raw 信息)。
    /// <para>owner 模型随后被 erase,SelItem 上的 native 读取(GetName 等)按 bridge
    /// owner_unresolved 语义降级返 null,不 crash。</para></summary>
    public ICreoModelItem? SelItem { get; }

    /// <summary>选中项的所属模型(对齐 <c>pfcSelection::GetSelModel</c>);owner 不可解析时 null。</summary>
    public CreoModel? SelModel { get; }

    /// <summary>装配组件路径(对齐 <c>pfcSelection::GetPath</c>,<c>ProSelectionAsmcomppathGet</c> 抽取);
    /// part/顶层场景或抽取失败为 null。可直接回喂 <see cref="CreoSelections.CreateModelItemSelection"/> 重建 selection。</summary>
    public CreoComponentPath? Path { get; }

    /// <summary>模型项 id(DATA 快照,<c>pro_model_item.id</c>;<see cref="SelItem"/>=null 时仍可读)。</summary>
    public int Id { get; }

    /// <summary>原始 <c>ProType</c> int(DATA 快照;Enum 未定义值不丢,诊断探针语义)。0=来源未填。</summary>
    public int RawTypeCode { get; }

    /// <summary>类型名解读:<see cref="CreoModelItemType"/> Enum 名,未定义值 "Unknown(N)"。</summary>
    public string TypeName { get; }

    /// <summary>高亮该项(对齐 <c>pfcSelection::Highlight</c>;经父集与 dispatcher;
    /// 父集已释放则抛 <see cref="ObjectDisposedException"/>)。</summary>
    public void Highlight() =>
        CreoSdkLog.Run(
            "selection", "highlight",
            body: () =>
            {
                _parent.EnsureUsable();
                _parent.HighlightItem(_index);
            },
            failProps: LogProps);

    /// <summary>取消高亮该项(对齐 <c>pfcSelection::UnHighlight</c>)。</summary>
    public void UnHighlight() =>
        CreoSdkLog.Run(
            "selection", "unhighlight",
            body: () =>
            {
                _parent.EnsureUsable();
                _parent.UnhighlightItem(_index);
            },
            failProps: LogProps);

    /// <summary>显示该项(对齐 <c>pfcSelection::Display</c>;一次性动作,下次 repaint 即擦除)。</summary>
    public void Display() =>
        CreoSdkLog.Run(
            "selection", "display",
            body: () =>
            {
                _parent.EnsureUsable();
                _parent.DisplayItem(_index);
            },
            failProps: LogProps);

    private object LogProps()
        => new { name = Label, type = SelItem?.Type.ToString() ?? "Unknown", index = _index };
}
