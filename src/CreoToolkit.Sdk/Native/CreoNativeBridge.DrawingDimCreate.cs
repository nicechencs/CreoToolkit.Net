using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProDrawingDimensionCreate(drawing, attachment_arr, dsense_arr, orient_hint,
    //                           location, ref_dim, ref dimension)
    // 图纸尺寸程序化创建(官方范式 UgDrawingDimensions.c + tkuse 手册 1325 段):
    //   - attachment_arr: ProArray of ProDimAttachment = ProSelection[2](每 attach 点
    //     2 连续指针,第二个 NULL=非 intersection);selection 必须 ProSelectionViewSet
    //     指定 drawing view(程序化 Alloc 的 selection 无 view 上下文)。
    //   - dsense_arr: ProArray of pro_dim_sense(type/sense/orient_hint,与 attach 等长)。
    //   - 入参数组本层构造本层释放:sel 逐个 ProSelectionFree + 两数组 ProArrayFree
    //     (数组元素只是指针拷贝,与 heap-out 读回场景的 ProDimattachmentarrayFree 不同,
    //     后者 free 的是 native 分配的 selection)。
    //   - CREATE_DRAWING_DIMS_ONLY config 决定 dim 存 solid 还是 drawing(tkuse 1325)。

    private const int DIM_ATTACH_PTRS_PER_PAIR = 2;

    /// <summary>按 view id 找 drawing view 句柄;找不到返 IntPtr.Zero。
    /// 与 DrawingViewIdList 同款 visit 模式(_visitRegistry 蹦床)。</summary>
    private IntPtr DrawingViewHandleById(IntPtr drawingMdl, int viewId)
    {
        var found = IntPtr.Zero;
        ProDrawingViewVisitAction cb = (drawing, view, filterStatus, _) =>
        {
            if (found != IntPtr.Zero) return GNS.ProErrors.PRO_TK_NO_ERROR;
            if (G.ProDrawingViewIdGet(drawing, view, out int id) == GNS.ProErrors.PRO_TK_NO_ERROR
                && id == viewId)
            {
                found = view;
            }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };
        var ptr = _visitRegistry.Register(cb);
        try
        {
            _ = G.ProDrawingViewVisit(drawingMdl, ptr, IntPtr.Zero, IntPtr.Zero);
        }
        finally { _visitRegistry.Unregister(cb); }
        return found;
    }

    /// <summary>程序化创建图纸尺寸(<c>ProDrawingDimensionCreate</c>)。
    /// attachItems 每项一个 attach 点(非 intersection);senses 与其等长;
    /// 全部 attach 关联到 <paramref name="viewId"/> 指定的 view。
    /// drawing/view 不存在返 null;native 创建失败(BAD_INPUTS/BAD_DIM_ATTACH)抛
    /// <see cref="CreoException"/>(写操作硬失败,不静默)。
    /// 返回新尺寸 ItemRef(type 以 native 回写为准:PRO_DIMENSION 或 PRO_REF_DIMENSION)。</summary>
    public unsafe ItemRef? DrawingDimensionCreate(
        ModelIdentity drawing,
        IReadOnlyList<ItemRef> attachItems,
        IReadOnlyList<DimSenseInfo> senses,
        int orientHint,
        (double X, double Y, double Z) location,
        bool refDim,
        int viewId)
    {
        if (attachItems.Count == 0 || attachItems.Count != senses.Count)
            throw new ArgumentException(
                $"attachItems({attachItems.Count}) 必须非空且与 senses({senses.Count}) 等长");

        if (!TryResolveMdl(drawing, out var dwgMdl)) return null;
        var view = DrawingViewHandleById(dwgMdl, viewId);
        if (view == IntPtr.Zero) return null;

        var sels = new IntPtr[attachItems.Count];
        IntPtr attachArr = IntPtr.Zero, senseArr = IntPtr.Zero;
        try
        {
            // 1. 逐 attach 造 selection(owner=各自模型)并挂 view 上下文
            for (int i = 0; i < attachItems.Count; i++)
            {
                var item = attachItems[i];
                if (!TryResolveMdl(item.Model, out var ownerMdl))
                    throw new InvalidOperationException(
                        $"attach 模型 {item.Model.Name} 不在会话中");
                if (!TryAllocSelection(ownerMdl, item, out sels[i]))
                    throw new InvalidOperationException(
                        $"attach {item.Type}#{item.Id} 无法构造 ProSelection(NotFound)");
                Check.Eval(nameof(G.ProSelectionViewSet),
                    G.ProSelectionViewSet(view, ref sels[i]));
            }

            // 2. attachment ProArray(元素=ProSelection[2],pair 第二指针 NULL)
            Check.Eval(nameof(G.ProArrayAlloc),
                G.ProArrayAlloc(0, DIM_ATTACH_PTRS_PER_PAIR * IntPtr.Size, 1, out attachArr));
            var pair = stackalloc IntPtr[DIM_ATTACH_PTRS_PER_PAIR];
            for (int i = 0; i < sels.Length; i++)
            {
                pair[0] = sels[i];
                pair[1] = IntPtr.Zero;
                Check.Eval(nameof(G.ProArrayObjectAdd),
                    G.ProArrayObjectAdd(ref attachArr, -1, 1, (IntPtr)pair));
            }

            // 3. sense ProArray
            Check.Eval(nameof(G.ProArrayAlloc),
                G.ProArrayAlloc(0, Marshal.SizeOf<GNS.pro_dim_sense>(), 1, out senseArr));
            for (int i = 0; i < senses.Count; i++)
            {
                var s = new GNS.pro_dim_sense
                {
                    type = (GNS.pro_dim_sense_type)senses[i].Type,
                    sense = senses[i].Sense,
                    orient_hint = (GNS.pro_dim_orient)senses[i].OrientHint,
                };
                Check.Eval(nameof(G.ProArrayObjectAdd),
                    G.ProArrayObjectAdd(ref senseArr, -1, 1, (IntPtr)(&s)));
            }

            // 4. 创建
            var loc = new[] { location.X, location.Y, location.Z };
            var dim = default(GNS.pro_model_item);
            Check.Eval(nameof(G.ProDrawingDimensionCreate),
                G.ProDrawingDimensionCreate(dwgMdl, attachArr, senseArr,
                    (GNS.pro_dim_orient)orientHint, loc,
                    refDim ? GNS.ProBooleans.PRO_B_TRUE : GNS.ProBooleans.PRO_B_FALSE,
                    ref dim));

            // 5. 显示(容错:init 上下文 Show 可能无效,失败记日志不致命)
            var showRc = TryShowDrawingAnnotation(ref dim, view);

            // 6. 归属解析:CREATE_DRAWING_DIMS_ONLY config 决定新尺寸存 drawing 还是关联
            //    solid(tkuse 1325;真机默认存 solid)。以 native 回写的 dim.owner 反查
            //    name/type 构造真实归属(不猜表:装配图纸场景 owner 可能是 attach 之外的
            //    solid),否则 ProDimensionDelete 等按 owner 定位的后续调用 BAD_INPUTS。
            var ownerIdentity = dim.owner == dwgMdl ? drawing : ResolveModelIdentity(dim.owner);
            if (ownerIdentity is null)
                CreoSdkLog.Trace("drawing", "dim.create.owner_unresolved",
                    new { drawing = drawing.Name, dimId = dim.id, owner = dim.owner.ToInt64() });
            var ownerModel = ownerIdentity ?? drawing;

            CreoSdkLog.Trace("drawing", "dim.create.ok",
                new { drawing = drawing.Name, viewId, attaches = attachItems.Count,
                      dimId = dim.id, dimType = (int)dim.type, showRc,
                      owner = ownerModel.Name, ownerIsDrawing = dim.owner == dwgMdl });
            return new ItemRef(ownerModel, (CreoModelItemType)(int)dim.type, dim.id);
        }
        finally
        {
            if (attachArr != IntPtr.Zero) _ = G.ProArrayFree(ref attachArr);
            if (senseArr != IntPtr.Zero) _ = G.ProArrayFree(ref senseArr);
            for (int i = 0; i < sels.Length; i++)
                if (sels[i] != IntPtr.Zero) _ = G.ProSelectionFree(ref sels[i]);
        }
    }

    /// <summary>mdl 句柄反查 ModelIdentity(ProMdlMdlnameGet + ProMdlTypeGet);失败返 null。</summary>
    private static ModelIdentity? ResolveModelIdentity(IntPtr mdl)
    {
        if (mdl == IntPtr.Zero) return null;
        var nameBuf = new char[180];
        if ((int)G.ProMdlMdlnameGet(mdl, nameBuf) != 0) return null;
        if ((int)G.ProMdlTypeGet(mdl, out var mdlType) != 0) return null;
        return new ModelIdentity(FromProName(nameBuf), FromProMdlType((int)mdlType));
    }

    /// <summary>ProAnnotationShow 容错版:rc 只作记录不抛(init 上下文 Show 常无效,
    /// 写主链成功不因 Show 失败回滚)。comp_path 按头文件契约传 NULL(顶层模型场景,
    /// 零结构体 ≠ NULL,走 GeneratedHelpers IntPtr 重载)。</summary>
    private static int TryShowDrawingAnnotation(ref GNS.pro_model_item dim, IntPtr view)
        => (int)GeneratedHelpers.ProAnnotationShow(ref dim, IntPtr.Zero, view);

    /// <summary>删除尺寸(<c>ProDimensionDelete</c>;solid/drawing 尺寸通用)。</summary>
    public void DimensionDelete(ItemRef dimension)
    {
        if (!TryResolveMdl(dimension.Model, out var mdl))
            throw new InvalidOperationException($"模型 {dimension.Model.Name} 不在会话中");
        var dim = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)dimension.Type,
            id = dimension.Id,
            owner = mdl,
        };
        Check.Eval(nameof(G.ProDimensionDelete), G.ProDimensionDelete(ref dim));
        CreoSdkLog.Trace("dimension", "delete.ok",
            new { model = dimension.Model.Name, id = dimension.Id });
    }

    // ---- 图纸坐标式尺寸(2026-07-06 续):UgDrawingDimensions.c 后半段。
    // ProDrawingOrdbaselineCreate:入参线性 dim 就地转 ordinate,新建 baseline(出参)。
    // ProDrawingDimToOrdinate:用已建 baseline 把另一线性 dim 就地转 ordinate。
    // ProDrawingDimIsOrdinate:读回是否 ordinate + 关联 baseline 反查。
    // owner 语义:族内不统一,尺寸类按定位形态设 owner(dim 归属模型的 mdl 句柄)。

    /// <summary>ProDrawingOrdbaselineCreate 副作用:把入参线性 dim **就地**转为 ordinate baseline,
    /// 并回写 baseline 项(tkuse 语义为"把指定尺寸转成 baseline",回写可能与入参同 id)。
    /// drawing 不在会话返 null;dim owner 模型不在会话抛
    /// <see cref="InvalidOperationException"/>(写路径);native 失败抛异常。<br/>
    /// location = **3D solid 坐标**下贴近欲作 baseline 端 attach 实体的点,用于选定尺寸
    /// 哪一端作 baseline(tkuse:close to the appropriate attachment entity, in 3D solid
    /// coordinates)。baseline.owner 反查:owner==drawing 用 drawing 身份,
    /// 否则 <see cref="ResolveModelIdentity"/>;unresolved 记 trace 后回退 drawing 身份。</summary>
    public ItemRef? DrawingOrdbaselineCreate(
        ModelIdentity drawing, ItemRef linearDim, (double X, double Y, double Z) location)
    {
        if (!TryResolveMdl(drawing, out var dwgMdl)) return null;
        if (!TryResolveMdl(linearDim.Model, out var dimOwnerMdl))
            throw new InvalidOperationException($"dim owner 模型 {linearDim.Model.Name} 不在会话中");

        var dim = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)linearDim.Type,
            id = linearDim.Id,
            owner = dimOwnerMdl,
        };
        var loc = new[] { location.X, location.Y, location.Z };
        var baseline = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProDrawingOrdbaselineCreate),
            G.ProDrawingOrdbaselineCreate(dwgMdl, ref dim, loc, ref baseline));

        var ownerIdentity = baseline.owner == dwgMdl ? drawing : ResolveModelIdentity(baseline.owner);
        if (ownerIdentity is null)
            CreoSdkLog.Trace("drawing", "dim.ordbaseline.owner_unresolved",
                new { drawing = drawing.Name, dimId = linearDim.Id, baselineId = baseline.id,
                      owner = baseline.owner.ToInt64() });
        var ownerModel = ownerIdentity ?? drawing;

        CreoSdkLog.Trace("drawing", "dim.ordbaseline.ok",
            new { drawing = drawing.Name, dimId = linearDim.Id,
                  baselineId = baseline.id, baselineType = (int)baseline.type,
                  owner = ownerModel.Name, ownerIsDrawing = baseline.owner == dwgMdl });
        return new ItemRef(ownerModel, (CreoModelItemType)(int)baseline.type, baseline.id);
    }

    /// <summary>ProDrawingDimToOrdinate 副作用:用已建 baseline 把入参线性 dim **就地**转 ordinate。
    /// drawing/owner 模型不在会话抛 <see cref="InvalidOperationException"/>(写路径);
    /// native 失败抛异常。</summary>
    public void DrawingDimToOrdinate(ModelIdentity drawing, ItemRef linearDim, ItemRef baseline)
    {
        if (!TryResolveMdl(drawing, out var dwgMdl))
            throw new InvalidOperationException($"drawing {drawing.Name} 不在会话中");
        if (!TryResolveMdl(linearDim.Model, out var dimOwnerMdl))
            throw new InvalidOperationException($"dim owner 模型 {linearDim.Model.Name} 不在会话中");
        if (!TryResolveMdl(baseline.Model, out var baseOwnerMdl))
            throw new InvalidOperationException($"baseline owner 模型 {baseline.Model.Name} 不在会话中");

        var dim = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)linearDim.Type,
            id = linearDim.Id,
            owner = dimOwnerMdl,
        };
        var base_ = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)baseline.Type,
            id = baseline.Id,
            owner = baseOwnerMdl,
        };
        Check.Eval(nameof(G.ProDrawingDimToOrdinate),
            G.ProDrawingDimToOrdinate(dwgMdl, ref dim, ref base_));
        CreoSdkLog.Trace("drawing", "dim.to_ordinate.ok",
            new { drawing = drawing.Name, dimId = linearDim.Id, baselineId = baseline.Id });
    }

    /// <summary>ProDrawingDimIsOrdinate 读回:是否 ordinate + 关联 baseline。<br/>
    /// drawing 不在会话 / dim owner 模型不在会话 / native 非 NO_ERROR → 返 null
    /// (读回族 miss 归 null 先例)。<br/>
    /// IsOrdinate=false 时 Baseline=null;IsOrdinate=true 时 Baseline 携 owner 反查。</summary>
    public (bool IsOrdinate, ItemRef? Baseline)? DrawingDimIsOrdinate(ModelIdentity drawing, ItemRef dim)
    {
        // null 早退带 reason 后缀(仓内先例:开发期定位 null 不瞎猜)
        if (!TryResolveMdl(drawing, out var dwgMdl))
        {
            CreoSdkLog.Trace("drawing", "dim.is_ordinate.drawing_missing",
                new { drawing = drawing.Name, dimId = dim.Id });
            return null;
        }
        if (!TryResolveMdl(dim.Model, out var dimOwnerMdl))
        {
            CreoSdkLog.Trace("drawing", "dim.is_ordinate.owner_missing",
                new { drawing = drawing.Name, dimModel = dim.Model.Name, dimId = dim.Id });
            return null;
        }

        var dimItem = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)dim.Type,
            id = dim.Id,
            owner = dimOwnerMdl,
        };
        var baseItem = default(GNS.pro_model_item);
        var rc = G.ProDrawingDimIsOrdinate(dwgMdl, ref dimItem, out var ordinate, ref baseItem);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Trace("drawing", "dim.is_ordinate.miss",
                new { drawing = drawing.Name, dimId = dim.Id, rc = (int)rc });
            return null;
        }

        var isOrd = ordinate == GNS.ProBooleans.PRO_B_TRUE;
        ItemRef? baselineRef = null;
        if (isOrd && baseItem.id != 0)
        {
            var ownerIdentity = baseItem.owner == dwgMdl ? drawing : ResolveModelIdentity(baseItem.owner);
            if (ownerIdentity is null)
                CreoSdkLog.Trace("drawing", "dim.is_ordinate.owner_unresolved",
                    new { drawing = drawing.Name, dimId = dim.Id, baselineId = baseItem.id,
                          owner = baseItem.owner.ToInt64() });
            var ownerModel = ownerIdentity ?? drawing;
            baselineRef = new ItemRef(ownerModel, (CreoModelItemType)(int)baseItem.type, baseItem.id);
        }
        CreoSdkLog.Trace("drawing", "dim.is_ordinate.ok",
            new { drawing = drawing.Name, dimId = dim.Id, isOrdinate = isOrd,
                  baselineId = baselineRef?.Id });
        return (isOrd, baselineRef);
    }

    /// <summary>ProDrawingDimToLinear 副作用:把 ordinate dim **就地**转回线性(tkuse 59537
    /// 明写:converts an existing ordinate dimension to linear)。<br/>
    /// drawing/dim owner 模型不在会话抛 <see cref="InvalidOperationException"/>(写路径);
    /// native 失败抛异常。owner 按同族「必须设置」侧处理。</summary>
    public void DrawingDimToLinear(ModelIdentity drawing, ItemRef ordinateDim)
    {
        if (!TryResolveMdl(drawing, out var dwgMdl))
            throw new InvalidOperationException($"drawing {drawing.Name} 不在会话中");
        if (!TryResolveMdl(ordinateDim.Model, out var dimOwnerMdl))
            throw new InvalidOperationException($"dim owner 模型 {ordinateDim.Model.Name} 不在会话中");

        var dim = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)ordinateDim.Type,
            id = ordinateDim.Id,
            owner = dimOwnerMdl,
        };
        Check.Eval(nameof(G.ProDrawingDimToLinear),
            G.ProDrawingDimToLinear(dwgMdl, ref dim));
        CreoSdkLog.Trace("drawing", "dim.to_linear.ok",
            new { drawing = drawing.Name, dimId = ordinateDim.Id });
    }
}
