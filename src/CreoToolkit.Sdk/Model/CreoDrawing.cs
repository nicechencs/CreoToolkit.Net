using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Native;

namespace CreoToolkit.Sdk;

/// <summary>2D 模型(Drawing):无 Regenerate 等实体操作。Drawing 独有能力。</summary>
public sealed class CreoDrawing : CreoModel
{
    internal CreoDrawing(CreoSession session, ModelIdentity id) : base(session, id) { }

    /// <summary>图纸总页数(ProDrawingSheetCount); 不支持返回 null。</summary>
    public int? GetSheetsCount()
        => _session.Run(n => n.DrawingSheetsCount(Identity));

    /// <summary>当前图纸页序号(1-based, ProDrawingCurrentSheetGet); 不支持返回 null。</summary>
    public int? GetCurrentSheetIndex()
        => _session.Run(n => n.DrawingCurrentSheet(Identity));

    /// <summary>图纸视图总数(ProDrawingViewsCount); 不支持返回 null。</summary>
    public int? GetViewsCount()
        => _session.Run(n => n.DrawingViewsCount(Identity));

    /// <summary>列出 drawing view id（ProDrawingViewVisit + ProDrawingViewIdGet）；返回真实 id 便于 fact log 校验 visitor payload。</summary>
    public IReadOnlyList<int> ListViewIds()
        => _session.Run(n => n.DrawingViewIdList(Identity));

    /// <summary>列出 drawing view DATA 快照(sheet/scale/name/solid 全收集)。
    /// 元数据提取失败的 view 被静默跳过；需感知跳过数用 <see cref="ListViewsWithSkip"/>。</summary>
    public IReadOnlyList<CreoDrawingView> ListViews()
        => ListViewsWithSkip().Views;

    /// <summary>同 <see cref="ListViews"/>，额外返回因元数据缺失被跳过的 view 数量。</summary>
    public CreoDrawingViewListResult ListViewsWithSkip()
    {
        var (views, skipped) = _session.Run(n => n.DrawingViewsList(Identity, _session.CurrentEpoch));
        CreoSdkLog.Trace("drawing", "list-views",
            new { drawing = Identity.Name, count = views.Count, skipped });
        return new CreoDrawingViewListResult(views, skipped);
    }

    // ---- CreoDrawing 新增方法 ----

    /// <summary>获取图纸关联的当前实体模型（Ctk_DrawingCurrentSolidGet）；无关联返回 null。</summary>
    public CreoSolid? GetCurrentSolid()
    {
        var solidId = _session.Run(n => n.DrawingCurrentSolid(Identity));
        if (solidId is null) return null;
        return CreoModels.CreateModel(_session, solidId.Value) as CreoSolid;
    }

    /// <summary>列出图纸关联的全部 part/assembly solid；无关联实体返回空列表。</summary>
    public IReadOnlyList<CreoSolid> ListSolids()
    {
        var identities = _session.Run(n => n.DrawingSolidsList(Identity));
        if (identities.Count == 0) return Array.Empty<CreoSolid>();

        var result = new CreoSolid[identities.Count];
        for (int i = 0; i < identities.Count; i++)
        {
            result[i] = CreoModels.CreateModel(_session, identities[i]) as CreoSolid
                ?? throw new InvalidOperationException(
                    $"Drawing solid visitor 返回了非 solid 模型类型: {identities[i].Type}");
        }
        return result;
    }

    // ---- P7 Drawing Visitor ----

    /// <summary>列出图纸表格(DATA 快照)；无则返空列表。</summary>
    public IReadOnlyList<ICreoModelItem> ListTables()
        => Materialize(_session.Run(n => n.DrawingTableList(Identity)));

    /// <summary>列出图纸尺寸(DATA 快照)；dimType=0 全部类型。</summary>
    public IReadOnlyList<ICreoModelItem> ListDimensions(int dimType = 0)
        => Materialize(_session.Run(n => n.DrawingDimensionList(Identity, dimType)));

    /// <summary>取图纸尺寸附着信息(<c>ProDrawingDimAttachpointsGet</c>):附着 pair 数 + 每 pair sense。<br/>
    /// 图纸尺寸的 annotation plane 该 API 无出参,总返 null AnnotationPlane。<br/>
    /// 不适用尺寸(driving/未 attach,native 返 BAD_INPUTS/INVALID_ITEM)统一归 null 不抛。<br/>
    /// <paramref name="dimension"/> 必须同 session:跨 session 抛 <see cref="InvalidOperationException"/>。<br/>
    /// 属**另一张 drawing** 的 dimension 返 null(query 契约);归属 solid 的 dimension 合法放行
    /// (drawing 建的尺寸默认存关联 solid),是否属本图由 native 判定,不适用即归 null。</summary>
    public DimensionAttachments? GetDimensionAttachments(CreoDimension dimension)
    {
        ThrowUtil.IfNull(dimension);
        dimension.EnsureBelongsTo(_session);
        // 归属校验:bridge 只传 type+id 给 native,不校验时跨 drawing 同 id 会读错对象。
        // 只拦"属另一张 drawing"的 dim;归属 solid 的 dim 合法
        // (drawing 建的尺寸默认存关联 solid,tkuse 1325),交 native 判定。
        if (dimension.Ref.Model != Identity && dimension.Ref.Model.Type == CreoModelType.Drawing)
        {
            CreoSdkLog.Trace("drawing", "getdimensionattachments.model_mismatch",
                new { drawing = Identity.Name, dimModel = dimension.Ref.Model.Name, dimId = dimension.Id });
            return null;
        }
        var raw = _session.Run(n => n.DrawingDimensionAttachmentsGet(Identity, dimension.Ref));
        CreoSdkLog.Trace("drawing", "getdimensionattachments",
            new { drawing = Identity.Name, dimId = dimension.Id, found = raw is not null });
        return raw is null ? null : CreoDimension.ToPublic(_session, raw);
    }

    /// <summary>程序化创建图纸尺寸(<c>ProDrawingDimensionCreate</c>,官方范式 UgDrawingDimensions.c)。<br/>
    /// <paramref name="attachItems"/> 每项一个 attach 点(非 intersection),与 <paramref name="senses"/>
    /// 等长配对,全部 attach 关联 <paramref name="viewId"/> 指定的 view。<br/>
    /// view 不存在返 null;native 创建失败(BAD_INPUTS/BAD_DIM_ATTACH)抛异常(写操作硬失败)。<br/>
    /// 返回 <see cref="CreoDimension"/> 或 <see cref="CreoRefDimension"/>(以 native 回写 type 为准:
    /// 头文件 ref_dim 注释与真机行为相反,false 实测建普通尺寸;
    /// <paramref name="asRefDimension"/>=true 路径尚未真机验证);
    /// 新尺寸天然携带 attachments,可经 <see cref="GetDimensionAttachments"/> 读回。</summary>
    public CreoModelItem? CreateDimension(
        int viewId,
        IReadOnlyList<CreoModelItem> attachItems,
        IReadOnlyList<DimSense> senses,
        int orientHint,
        CreoVec3 location,
        bool asRefDimension = false)
    {
        ThrowUtil.IfNull(attachItems);
        ThrowUtil.IfNull(senses);
        if (attachItems.Count == 0 || attachItems.Count != senses.Count)
            throw new ArgumentException(
                $"attachItems({attachItems.Count}) 必须非空且与 senses({senses.Count}) 等长");
        var refs = new ItemRef[attachItems.Count];
        for (int i = 0; i < attachItems.Count; i++)
        {
            ThrowUtil.IfNull(attachItems[i]);
            attachItems[i].EnsureBelongsTo(_session);
            refs[i] = attachItems[i].Ref;
        }
        var senseInfos = senses.Select(s => new DimSenseInfo(s.Type, s.Sense, s.OrientHint)).ToArray();
        var created = _session.Run(n => n.DrawingDimensionCreate(
            Identity, refs, senseInfos, orientHint,
            (location.X, location.Y, location.Z), asRefDimension, viewId));
        CreoSdkLog.Trace("drawing", "createdimension",
            new { drawing = Identity.Name, viewId, attaches = refs.Length,
                  created = created is not null, dimId = created?.Id, dimType = created?.Type });
        return created is null ? null : CreoModelItem.CreateFromRef(_session, created.Value);
    }

    /// <summary>校验入参是尺寸类型(Dimension/RefDimension),防 Csys/Feature 一路打进 native。
    /// 与 <see cref="DeleteDimension"/> 同款守卫。</summary>
    private static void EnsureDimensionType(CreoModelItem item, string role)
    {
        if (item.Type is not (CreoModelItemType.Dimension or CreoModelItemType.RefDimension))
            throw new ArgumentException($"类型 {item.Type} 不是尺寸,不可作 {role}");
    }

    /// <summary>把线性尺寸**就地**转成 ordinate 并新建 baseline(<c>ProDrawingOrdbaselineCreate</c>,
    /// 官方范式 UgDrawingDimensions.c 后半段)。<br/>
    /// <b>副作用</b>:入参 <paramref name="linearDim"/> 会被 native 就地改成 ordinate 尺寸,不是 Create;
    /// <paramref name="nearAttachPoint"/> 为 **3D solid 坐标**下贴近欲作 baseline 端 attach 实体的点,
    /// 作用是选定尺寸哪一端作 baseline(tkuse:close to the appropriate attachment entity,
    /// in 3D solid coordinates;官方例基线端为 csys)。<br/>
    /// drawing 不在会话返 null;native 失败抛异常(写路径硬失败);非尺寸类型抛
    /// <see cref="ArgumentException"/>。<br/>
    /// 返回 baseline 的 <see cref="CreoModelItem"/>(以 native 回写为准;tkuse 语义为
    /// "把指定尺寸转成 ordinate baseline",回写项可能与 <paramref name="linearDim"/> 同 id)。</summary>
    public CreoModelItem? ConvertToOrdinateBaseline(CreoModelItem linearDim, CreoVec3 nearAttachPoint)
    {
        ThrowUtil.IfNull(linearDim);
        linearDim.EnsureBelongsTo(_session);
        EnsureDimensionType(linearDim, "ordinate 转换对象");
        // 归属校验:归属另一张 drawing 的 dim 拦(与 GetDimensionAttachments 一致);
        // 归属 solid 的 dim 放行(drawing 建的尺寸默认存关联 solid,tkuse 1325)。
        if (linearDim.Ref.Model != Identity && linearDim.Ref.Model.Type == CreoModelType.Drawing)
            throw new InvalidOperationException(
                $"dim {linearDim.Ref.Model.Name}#{linearDim.Id} 属另一张 drawing,不能在 {Identity.Name} 上转 ordinate");

        var baseline = _session.Run(n => n.DrawingOrdbaselineCreate(
            Identity, linearDim.Ref,
            (nearAttachPoint.X, nearAttachPoint.Y, nearAttachPoint.Z)));
        CreoSdkLog.Trace("drawing", "converttoordinatebaseline",
            new { drawing = Identity.Name, dimId = linearDim.Id,
                  created = baseline is not null, baselineId = baseline?.Id });
        return baseline is null ? null : CreoModelItem.CreateFromRef(_session, baseline.Value);
    }

    /// <summary>用已建 baseline 把另一线性尺寸**就地**转成 ordinate(<c>ProDrawingDimToOrdinate</c>)。<br/>
    /// <b>副作用</b>:入参 <paramref name="linearDim"/> 会被 native 就地改成 ordinate。<br/>
    /// drawing/dim/baseline 归属另一张 drawing 拦(solid-owned 放行);非尺寸类型抛
    /// <see cref="ArgumentException"/>;native 失败抛异常(写路径硬失败)。</summary>
    public void ConvertToOrdinate(CreoModelItem linearDim, CreoModelItem baseline)
    {
        ThrowUtil.IfNull(linearDim);
        ThrowUtil.IfNull(baseline);
        linearDim.EnsureBelongsTo(_session);
        baseline.EnsureBelongsTo(_session);
        EnsureDimensionType(linearDim, "ordinate 转换对象");
        EnsureDimensionType(baseline, "ordinate baseline");
        if (linearDim.Ref.Model != Identity && linearDim.Ref.Model.Type == CreoModelType.Drawing)
            throw new InvalidOperationException(
                $"dim {linearDim.Ref.Model.Name}#{linearDim.Id} 属另一张 drawing,不能在 {Identity.Name} 上转 ordinate");
        if (baseline.Ref.Model != Identity && baseline.Ref.Model.Type == CreoModelType.Drawing)
            throw new InvalidOperationException(
                $"baseline {baseline.Ref.Model.Name}#{baseline.Id} 属另一张 drawing,不能作 {Identity.Name} 的 baseline");

        _session.Run(n => { n.DrawingDimToOrdinate(Identity, linearDim.Ref, baseline.Ref); return 0; });
        CreoSdkLog.Trace("drawing", "converttoordinate",
            new { drawing = Identity.Name, dimId = linearDim.Id, baselineId = baseline.Id });
    }

    /// <summary>读回尺寸是否为 ordinate + 关联 baseline(<c>ProDrawingDimIsOrdinate</c>)。<br/>
    /// drawing 不在会话 / native 读回失败 → 返 null(读回族 miss 归 null 先例);<br/>
    /// IsOrdinate=false 时 Baseline=null;true 时 Baseline 为 <see cref="CreoModelItem"/> 反查。<br/>
    /// <paramref name="dimension"/> 归属另一张 drawing 的拦;solid-owned 放行;
    /// 非尺寸类型抛 <see cref="ArgumentException"/>。</summary>
    public (bool IsOrdinate, CreoModelItem? Baseline)? GetOrdinateInfo(CreoModelItem dimension)
    {
        ThrowUtil.IfNull(dimension);
        dimension.EnsureBelongsTo(_session);
        EnsureDimensionType(dimension, "ordinate 查询对象");
        if (dimension.Ref.Model != Identity && dimension.Ref.Model.Type == CreoModelType.Drawing)
        {
            CreoSdkLog.Trace("drawing", "getordinateinfo.model_mismatch",
                new { drawing = Identity.Name, dimModel = dimension.Ref.Model.Name, dimId = dimension.Id });
            return null;
        }

        var raw = _session.Run(n => n.DrawingDimIsOrdinate(Identity, dimension.Ref));
        CreoSdkLog.Trace("drawing", "getordinateinfo",
            new { drawing = Identity.Name, dimId = dimension.Id,
                  found = raw is not null, isOrd = raw?.IsOrdinate,
                  baselineId = raw?.Baseline?.Id });
        if (raw is null) return null;
        var (isOrd, baseline) = raw.Value;
        CreoModelItem? baselineItem = baseline is null
            ? null : CreoModelItem.CreateFromRef(_session, baseline.Value);
        return (isOrd, baselineItem);
    }

    /// <summary>把 ordinate 尺寸**就地**转回线性(<c>ProDrawingDimToLinear</c>;tkuse 59537 明写:
    /// converts an existing ordinate dimension to linear)。<br/>
    /// <b>副作用</b>:入参 <paramref name="ordinateDim"/> 会被 native 就地改回线性尺寸,
    /// 不是 Create。<br/>
    /// drawing 不在会话或 native 失败抛异常(写路径硬失败);非尺寸类型抛
    /// <see cref="ArgumentException"/>;<paramref name="ordinateDim"/> 归属另一张 drawing
    /// 拦(solid-owned 放行)。</summary>
    public void ConvertToLinear(CreoModelItem ordinateDim)
    {
        ThrowUtil.IfNull(ordinateDim);
        ordinateDim.EnsureBelongsTo(_session);
        EnsureDimensionType(ordinateDim, "linear 转换对象");
        if (ordinateDim.Ref.Model != Identity && ordinateDim.Ref.Model.Type == CreoModelType.Drawing)
            throw new InvalidOperationException(
                $"dim {ordinateDim.Ref.Model.Name}#{ordinateDim.Id} 属另一张 drawing,不能在 {Identity.Name} 上转 linear");

        _session.Run(n => { n.DrawingDimToLinear(Identity, ordinateDim.Ref); return 0; });
        CreoSdkLog.Trace("drawing", "converttolinear",
            new { drawing = Identity.Name, dimId = ordinateDim.Id });
    }

    /// <summary>删除尺寸(<c>ProDimensionDelete</c>),与 <see cref="CreateDimension"/> 配对收尾。<br/>
    /// 归属以 dimension 自带 ItemRef 为准(drawing 建的尺寸默认存关联 solid,tkuse 1325,
    /// create 已按 native 回写 owner 解析)。写操作硬失败契约:非尺寸类型 / native 删除失败均抛异常。</summary>
    public void DeleteDimension(CreoModelItem dimension)
    {
        ThrowUtil.IfNull(dimension);
        dimension.EnsureBelongsTo(_session);
        if (dimension.Type is not (CreoModelItemType.Dimension or CreoModelItemType.RefDimension))
            throw new ArgumentException($"类型 {dimension.Type} 不是尺寸,不可经 DeleteDimension 删除");
        _session.Run(n => { n.DimensionDelete(dimension.Ref); return true; });
        CreoSdkLog.Trace("drawing", "deletedimension",
            new { drawing = Identity.Name, owner = dimension.Ref.Model.Name, dimId = dimension.Id });
    }

    /// <summary>列出详细注释(DATA 快照)；sheet=0 全部页。</summary>
    public IReadOnlyList<ICreoModelItem> ListDetailNotes(int sheet = 0)
        => Materialize(CollectAllSheets(sheet, s => _session.Run(n => n.DrawingDtlnoteList(Identity, s))));

    /// <summary>列出 detail symbol instance；sheet=0 表示遍历全部页。</summary>
    public IReadOnlyList<CreoDetailSymbolInstance> ListDetailSymbolInstances(int sheet = 0)
    {
        var refs = CollectAllSheets(
            sheet,
            s => _session.Run(n => n.DrawingDetailSymbolInstancesList(Identity, s)));
        if (refs.Count == 0) return Array.Empty<CreoDetailSymbolInstance>();

        var result = new CreoDetailSymbolInstance[refs.Count];
        for (int i = 0; i < refs.Count; i++)
        {
            var itemRef = refs[i];
            if (itemRef.Type != CreoModelItemType.SymbolInstance)
                itemRef = new ItemRef(itemRef.Model, CreoModelItemType.SymbolInstance, itemRef.Id);
            result[i] = new CreoDetailSymbolInstance(_session, itemRef, _session.CurrentEpoch);
        }
        return result;
    }

    /// <summary>列出 detail groups(<c>ProDrawingDtlgroupsCollect</c> 容器项, DATA 快照); sheet=0 全部页。
    /// <para>用途:Sketch tab 交互式画的 draft 几何(圆/圆弧/椭圆等) 在 view-related 归属下
    /// 常经 detail group 承载;<see cref="ListDtlentities"/>(symbol=NULL) 收不到 view-related 时用本 API 反查。</para></summary>
    public IReadOnlyList<ICreoModelItem> ListDetailGroups(int sheet = 0)
        => Materialize(CollectAllSheets(sheet, s => _session.Run(n => n.DrawingDtlgroupsList(Identity, s))));

    /// <summary>sheet=0 = "全部页"语义在 SDK 层实现:遍历 sheet 1..SheetsCount 逐页 native 调用拼合。
    /// <para>Why:Creo4 native <c>ProDrawingDtlentitiesCollect / ProDrawingDtlnoteVisit</c> 的 <c>sheet</c> 参数
    /// 是 1-based 具体页号,不接受 0 作"all"(sheet=0 真机返空,而 view-related draft entity 落 sheet 1
    /// 时被漏收)——2026-07-07 真机验证。</para>
    /// <para>sheet&gt;0 透传单页;GetSheetsCount 不可用(null/≤0)时回退直调 native 保向后兼容。</para></summary>
    private IReadOnlyList<T> CollectAllSheets<T>(int sheet, Func<int, IReadOnlyList<T>> perSheet)
    {
        if (sheet > 0) return perSheet(sheet);
        var count = GetSheetsCount();
        if (count is null || count <= 0)
        {
            // 回退直调 native sheet=0——真机已证该口径返空(见上注释),此路径只在
            // SheetsCount 不可用(真机未观测过)时触达;必须 warn 可观测,不许静默空
            CreoSdkLog.Warn(
                "drawing", "collect-all-sheets.sheets_count_unavailable",
                "GetSheetsCount 不可用,sheet=0 全页语义降级为 native 直调(真机口径返空)",
                new { drawing = Identity.Name, count });
            return perSheet(0);
        }
        if (count == 1) return perSheet(1);
        var combined = new List<T>();
        for (int i = 1; i <= count.Value; i++)
        {
            var page = perSheet(i);
            if (page.Count > 0) combined.AddRange(page);
        }
        return combined;
    }

    // ---- pt_drawing draft entity 暴露 ----

    /// <summary>列出图纸/页内所有 draft entities (<c>ProDrawingDtlentitiesCollect</c>, DATA 快照)。
    /// <para><paramref name="sheet"/>=0 收集所有页 entities; <paramref name="sheet"/>&gt;0 限定该页;
    /// <paramref name="sheet"/>&lt;0 抛 <see cref="ArgumentOutOfRangeException"/>。</para>
    /// <para>内部经 ProArrayFree 立即释放, L3 出口纯 DATA 快照 (<see cref="CreoDtlentity"/>)。
    /// 模型无 entities 返空列表 (不抛)。</para>
    /// <para><b>OOM 行为</b>: 大模型 OUT_OF_MEMORY 直接抛 <see cref="Errors.CreoException"/>
    /// (ErrorCode=OutOfMemory), 不映射为 NotFound; 业务侧自决降级。</para>
    /// <para>返出的 <see cref="CreoDtlentity"/> 携带当前 session 的 epoch,
    /// 跨 session 误用经 <see cref="GetDtlentityData"/> guard 拒绝。</para></summary>
    public IReadOnlyList<CreoDtlentity> ListDtlentities(int sheet = 0)
    {
        if (sheet < 0)
        {
            CreoSdkLog.Warn(
                "drawing", "dtlentity-list.invalid_sheet",
                "sheet 必须 >= 0 (0 = 全部页);业务侧误传负值",
                new { drawing = Identity.Name, sheet });
            throw new ArgumentOutOfRangeException(nameof(sheet), sheet, "sheet 必须 >= 0 (0 = 全部页)。");
        }

        var epoch = _session.CurrentEpoch;
        var result = CollectAllSheets(sheet, s => _session.Run(n => n.DrawingDtlentitiesList(Identity, s, epoch)));
        CreoSdkLog.Trace("drawing", "dtlentity-list",
            new { drawing = Identity.Name, sheet, count = result.Count });
        return result;
    }

    // ---- 类型化明细项列举(CreoDetailItem 族) ----

    /// <summary>列出草图实体(类型化 <see cref="CreoDraftEntity"/>)。
    /// 语义同 <see cref="ListDtlentities"/> 但出口为 session-bound 领域对象,
    /// 可经 <see cref="CreoDraftEntity.ToToken"/> 重建裸 token 供 <see cref="GetDtlentityData"/> 传入。</summary>
    public IReadOnlyList<CreoDraftEntity> ListDraftEntityItems(int sheet = 0)
    {
        if (sheet < 0)
        {
            CreoSdkLog.Warn(
                "drawing", "list-draft-entity-items.invalid_sheet",
                "sheet 必须 >= 0 (0 = 全部页);业务侧误传负值",
                new { drawing = Identity.Name, sheet });
            throw new ArgumentOutOfRangeException(nameof(sheet), sheet, "sheet 必须 >= 0 (0 = 全部页)。");
        }

        var epoch = _session.CurrentEpoch;
        var tokens = CollectAllSheets(sheet, s => _session.Run(n => n.DrawingDtlentitiesList(Identity, s, epoch)));
        if (tokens.Count == 0) return Array.Empty<CreoDraftEntity>();
        var result = new CreoDraftEntity[tokens.Count];
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            result[i] = new CreoDraftEntity(
                _session,
                new ItemRef(Identity, t.Type, t.Id),
                epoch);
        }
        CreoSdkLog.Trace("drawing", "list-draft-entity-items",
            new { drawing = Identity.Name, sheet, count = result.Length });
        return result;
    }

    /// <summary>列出详细注释(类型化 <see cref="CreoDetailNote"/>)。
    /// 语义同 <see cref="ListDetailNotes"/> 但出口为 session-bound 领域对象。</summary>
    public IReadOnlyList<CreoDetailNote> ListDetailNoteItems(int sheet = 0)
    {
        if (sheet < 0)
        {
            CreoSdkLog.Warn(
                "drawing", "list-detail-note-items.invalid_sheet",
                "sheet 必须 >= 0 (0 = 全部页);业务侧误传负值",
                new { drawing = Identity.Name, sheet });
            throw new ArgumentOutOfRangeException(nameof(sheet), sheet, "sheet 必须 >= 0 (0 = 全部页)。");
        }

        var epoch = _session.CurrentEpoch;
        var refs = CollectAllSheets(sheet, s => _session.Run(n => n.DrawingDtlnoteList(Identity, s)));
        if (refs.Count == 0) return Array.Empty<CreoDetailNote>();
        var result = new CreoDetailNote[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = new CreoDetailNote(_session, refs[i], epoch);
        CreoSdkLog.Trace("drawing", "list-detail-note-items",
            new { drawing = Identity.Name, sheet, count = result.Length });
        return result;
    }

    /// <summary>列出图纸表格(类型化 <see cref="CreoDrawingTable"/>)。
    /// 语义同 <see cref="ListTables"/> 但出口为 session-bound 领域对象。</summary>
    public IReadOnlyList<CreoDrawingTable> ListDrawingTableItems()
    {
        var epoch = _session.CurrentEpoch;
        var refs = _session.Run(n => n.DrawingTableList(Identity));
        if (refs.Count == 0) return Array.Empty<CreoDrawingTable>();
        var result = new CreoDrawingTable[refs.Count];
        for (int i = 0; i < refs.Count; i++)
            result[i] = new CreoDrawingTable(_session, refs[i], epoch);
        CreoSdkLog.Trace("drawing", "list-drawing-table-items",
            new { drawing = Identity.Name, count = result.Length });
        return result;
    }

    /// <summary>读 draft entity 详细数据 (<c>ProDtlentityDataGet</c> → <c>ProDtlentitydata</c>, DATA 快照)。
    /// <para>L3 接缝在 bridge 立即 <c>ProDtlentitydataFree</c> 释放, 出口纯 DATA (<see cref="CreoDtlentityData"/>)。</para>
    /// <para>读不到返 null (owner 已 erase / 同名跨 Load id 失效 / 3 sub-Get 失败 等场景);
    /// bridge trace 含 reason 区分 (W2)。</para>
    /// <para><paramref name="entity"/> 未初始化 (default 哨兵) 抛 <see cref="InvalidOperationException"/>;
    /// 来自异 session (epoch 不匹配) 同样抛 <see cref="InvalidOperationException"/>。</para></summary>
    public CreoDtlentityData? GetDtlentityData(CreoDtlentity entity)
    {
        CreoDtlentityGuard.NotUninitialized(entity, nameof(entity));
        CreoDtlentityGuard.SameSessionEpoch(entity, _session.CurrentEpoch, nameof(entity));

        var result = _session.Run(n => n.DrawingDtlentityDataGet(Identity, entity));
        CreoSdkLog.Trace("drawing", "dtlentity-data-get",
            new { drawing = Identity.Name, entityId = entity.Id, found = result.HasValue });
        return result;
    }

    /// <summary>按 id 区间探测可达的 draft entities(逐 id <c>ProDtlentityDataGet</c>,
    /// 读到数据即存在)。返回可达项的 (token, data) 对。
    /// <para>Why:view-related draft entity(归属 view 的手画/程序化实体)
    /// <c>ProDrawingDtlentitiesCollect</c> 一律收不到,
    /// 而 DataGet 按 (owner,id) 直取不走 Collect——
    /// 本 API 是 view-related 实体唯一的程序化读回路径。</para>
    /// <para><paramref name="maxId"/> 是探测上限(含);id 从 0 起密集分配,
    /// fixture 级模型 63 已远够。区间非法抛 <see cref="ArgumentOutOfRangeException"/>。</para></summary>
    public IReadOnlyList<(CreoDtlentity Entity, CreoDtlentityData Data)> ProbeDtlentities(int maxId = 63)
    {
        if (maxId < 0)
            throw new ArgumentOutOfRangeException(nameof(maxId), maxId, "maxId 必须 >= 0。");

        var epoch = _session.CurrentEpoch;
        var found = new List<(CreoDtlentity, CreoDtlentityData)>();
        for (int id = 0; id <= maxId; id++)
        {
            var token = new CreoDtlentity(
                Identity.Name, Identity.Type, CreoModelItemType.DraftEntity, id, epoch);
            var data = _session.Run(n => n.DrawingDtlentityDataGet(Identity, token));
            if (data.HasValue) found.Add((token, data.Value));
        }
        CreoSdkLog.Trace("drawing", "dtlentity-probe",
            new { drawing = Identity.Name, maxId, count = found.Count });
        return found;
    }

    // ---- Table 写路径 ----

    /// <summary>在当前图纸上创建表格。返回创建的 table ItemRef(type + id)。</summary>
    public CreoDrawingTable CreateTable(DrawingTableSpec spec)
    {
        ThrowUtil.IfNull(spec);
        var itemRef = _session.Run(n => n.DrawingTableCreate(Identity, spec));
        var epoch = _session.CurrentEpoch;
        CreoSdkLog.Trace("drawing", "create-table",
            new { drawing = Identity.Name, tableId = itemRef.Id });
        return new CreoDrawingTable(_session, itemRef, epoch);
    }

    /// <summary>读取表格单元格文本(mode=1 显示态);空格返空数组。</summary>
    public string[] ReadTableCellText(int tableId, int column, int row)
    {
        var lines = _session.Run(n => n.DrawingTableCellRead(Identity, tableId, column, row));
        CreoSdkLog.Trace("drawing", "read-table-cell",
            new { drawing = Identity.Name, tableId, column, row, lineCount = lines.Length });
        return lines;
    }

    /// <summary>删除表格。</summary>
    public void DeleteTable(int tableId)
    {
        _session.Run(n => { n.DrawingTableDelete(Identity, tableId); return 0; });
        CreoSdkLog.Trace("drawing", "delete-table",
            new { drawing = Identity.Name, tableId });
    }

    /// <summary>查询表格行数。</summary>
    public int GetTableRowsCount(int tableId)
        => _session.Run(n => n.DrawingTableRowsCount(Identity, tableId));

    /// <summary>查询表格列数。</summary>
    public int GetTableColumnsCount(int tableId)
        => _session.Run(n => n.DrawingTableColumnsCount(Identity, tableId));

    /// <summary>在图纸上创建一条 draft entity (<c>ProDtlentityCreate</c>, Create 方向)。
    /// <para>curve 几何按 <see cref="CreoDtlentityCurve"/> 的 Kind→字段映射表提供;
    /// 缺失必填几何字段抛 <see cref="ArgumentException"/>。</para>
    /// <para>返回创建的 entity id; native 失败返 null (bridge trace 含 reason)。
    /// 支持 Line/Arc/Circle/Point/Ellipse/Spline/BSpline; 其余 Kind 返 null。</para></summary>
    public int? CreateDraftEntity(CreoDtlentityCurve curve, CreoColor color)
    {
        var result = _session.Run(n => n.DrawingDtlentityCreate(Identity, curve, color));
        CreoSdkLog.Trace("drawing", "dtlentity-create",
            new { drawing = Identity.Name, kind = curve.Kind.ToString(), entityId = result });
        return result;
    }
}
