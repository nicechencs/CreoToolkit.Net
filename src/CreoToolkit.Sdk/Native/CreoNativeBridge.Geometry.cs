using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- Edge (LIVE handle 系,DHandle 衍生) ----

    /// <summary>ProEdgeInit + ProEdgeTypeGet:pro_ent_type 出参;不可读返 null。
    /// ProEdge handle BORROWED(库 static),不 free。</summary>
    public int? EdgeTypeGet(ItemRef edge)
    {
        if (!TryResolveMdl(edge.Model, out var mdl))
        {
            CreoSdkLog.Trace("edge", "typeget.owner_unresolved",
                new { model = edge.Model.Name, id = edge.Id });
            return null;
        }
        int st = (int)G.ProEdgeInit(mdl, edge.Id, out var pEdge);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "typeget.init_notfound",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return null;
        }
        st = (int)G.ProEdgeTypeGet(pEdge, out var edgeType);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "typeget.type_failed",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProEdgeTypeGet), st);
        return (int)edgeType;
    }

    /// <summary>ProEdgeInit + ProEdgeLengthEval:double 出参;不可读返 null。</summary>
    public double? EdgeLengthGet(ItemRef edge)
    {
        if (!TryResolveMdl(edge.Model, out var mdl))
        {
            CreoSdkLog.Trace("edge", "lengthget.owner_unresolved",
                new { model = edge.Model.Name, id = edge.Id });
            return null;
        }
        int st = (int)G.ProEdgeInit(mdl, edge.Id, out var pEdge);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "lengthget.init_notfound",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return null;
        }
        st = (int)G.ProEdgeLengthEval(pEdge, out var length);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "lengthget.length_failed",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProEdgeLengthEval), st);
        return length;
    }

    /// <summary>ProEdgeInit + ProEdgeNeighborsGet + ProSurfaceIdGet:两邻接面 id 转 (Face1, Face2)。
    /// 头文件返 4 handle(edge1/edge2/face1/face2);本方法只取两 face(邻接 surface),edge1/edge2 舍弃。
    /// 任一 face 反查 id 失败降级 null(不丢另一个)。ProEdge/ProSurface handle 均 BORROWED,不 free。</summary>
    public (ItemRef? Face1, ItemRef? Face2) EdgeNeighborSurfacesGet(ItemRef edge)
    {
        if (!TryResolveMdl(edge.Model, out var mdl))
        {
            CreoSdkLog.Trace("edge", "neighborsurfaces.owner_unresolved",
                new { model = edge.Model.Name, id = edge.Id });
            return (null, null);
        }
        int st = (int)G.ProEdgeInit(mdl, edge.Id, out var pEdge);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "neighborsurfaces.init_notfound",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return (null, null);
        }
        st = (int)G.ProEdgeNeighborsGet(pEdge, out _, out _, out var pFace1, out var pFace2);
        if (st < 0)
        {
            CreoSdkLog.Trace("edge", "neighborsurfaces.neighbors_failed",
                new { model = edge.Model.Name, id = edge.Id, rc = st });
            return (null, null);
        }
        Ensure(nameof(G.ProEdgeNeighborsGet), st);

        var face1 = ResolveFaceItemRef(pFace1, edge.Model);
        var face2 = ResolveFaceItemRef(pFace2, edge.Model);
        return (face1, face2);
    }

    private static ItemRef? ResolveFaceItemRef(IntPtr pSurface, ModelIdentity model)
    {
        if (pSurface == IntPtr.Zero) return null;
        int st = (int)G.ProSurfaceIdGet(pSurface, out var surfaceId);
        if (st < 0) return null;
        return new ItemRef(model, CreoModelItemType.Surface, surfaceId);
    }

    // ---- Curve (LIVE handle 系,DHandle 衍生) ----

    /// <summary>ProCurveInit + ProCurveLengthEval:double 出参;不可读返 null。</summary>
    public double? CurveLengthGet(ItemRef curve)
    {
        if (!TryResolveMdl(curve.Model, out var mdl))
        {
            CreoSdkLog.Trace("curve", "lengthget.owner_unresolved",
                new { model = curve.Model.Name, id = curve.Id });
            return null;
        }
        int st = (int)G.ProCurveInit(mdl, curve.Id, out var pCurve);
        if (st < 0)
        {
            CreoSdkLog.Trace("curve", "lengthget.init_notfound",
                new { model = curve.Model.Name, id = curve.Id, rc = st });
            return null;
        }
        st = (int)G.ProCurveLengthEval(pCurve, out var length);
        if (st < 0)
        {
            CreoSdkLog.Trace("curve", "lengthget.length_failed",
                new { model = curve.Model.Name, id = curve.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProCurveLengthEval), st);
        return length;
    }

    /// <summary>ProCurveInit + ProCurveTypeGet:pro_ent_type 出参;不可读返 null。</summary>
    public int? CurveTypeGet(ItemRef curve)
    {
        if (!TryResolveMdl(curve.Model, out var mdl))
        {
            CreoSdkLog.Trace("curve", "typeget.owner_unresolved",
                new { model = curve.Model.Name, id = curve.Id });
            return null;
        }
        int st = (int)G.ProCurveInit(mdl, curve.Id, out var pCurve);
        if (st < 0)
        {
            CreoSdkLog.Trace("curve", "typeget.init_notfound",
                new { model = curve.Model.Name, id = curve.Id, rc = st });
            return null;
        }
        st = (int)G.ProCurveTypeGet(pCurve, out var curveType);
        if (st < 0)
        {
            CreoSdkLog.Trace("curve", "typeget.type_failed",
                new { model = curve.Model.Name, id = curve.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProCurveTypeGet), st);
        return (int)curveType;
    }

    // ---- Quilt (LIVE handle 系,DHandle 衍生) ----

    /// <summary>ProQuiltInit + ProQuiltSurfaceVisit + ProSurfaceIdGet:枚举 quilt 上所有 surface。
    /// 无 surface 或不可读返空列表。ProSurface handle 归 BORROWED。</summary>
    public IReadOnlyList<ItemRef> QuiltSurfaceList(ItemRef quilt)
    {
        if (!TryResolveMdl(quilt.Model, out var mdl)) return Array.Empty<ItemRef>();
        int initSt = (int)G.ProQuiltInit(mdl, quilt.Id, out var pQuilt);
        if (initSt < 0)
        {
            CreoSdkLog.Trace("quilt", "surfacelist.init_notfound",
                new { model = quilt.Model.Name, id = quilt.Id, rc = initSt });
            return Array.Empty<ItemRef>();
        }
        return VisitCollect<ItemRef>(
            (h, action, filter, data) => G.ProQuiltSurfaceVisit(h, action, filter, data),
            pQuilt,
            (item, _) =>
            {
                if ((int)G.ProSurfaceIdGet(item, out var id) != 0) return null;
                return new ItemRef(quilt.Model, CreoModelItemType.Surface, id);
            });
    }

    /// <summary>ProQuiltInit + ProQuiltVolumeEval:double 出参;不可算返 null。</summary>
    public double? QuiltVolumeEval(ItemRef quilt)
    {
        if (!TryResolveMdl(quilt.Model, out var mdl))
        {
            CreoSdkLog.Trace("quilt", "volumeeval.owner_unresolved",
                new { model = quilt.Model.Name, id = quilt.Id });
            return null;
        }
        int st = (int)G.ProQuiltInit(mdl, quilt.Id, out var pQuilt);
        if (st < 0)
        {
            CreoSdkLog.Trace("quilt", "volumeeval.init_notfound",
                new { model = quilt.Model.Name, id = quilt.Id, rc = st });
            return null;
        }
        st = (int)G.ProQuiltVolumeEval(pQuilt, out var volume);
        if (st < 0)
        {
            CreoSdkLog.Trace("quilt", "volumeeval.volume_failed",
                new { model = quilt.Model.Name, id = quilt.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProQuiltVolumeEval), st);
        return volume;
    }

    /// <summary>ProQuiltInit + ProQuiltIsBackupgeometry:bool 出参;不可判返 null。</summary>
    public bool? QuiltIsBackupGeometry(ItemRef quilt)
    {
        if (!TryResolveMdl(quilt.Model, out var mdl))
        {
            CreoSdkLog.Trace("quilt", "isbackupgeometry.owner_unresolved",
                new { model = quilt.Model.Name, id = quilt.Id });
            return null;
        }
        int st = (int)G.ProQuiltInit(mdl, quilt.Id, out var pQuilt);
        if (st < 0)
        {
            CreoSdkLog.Trace("quilt", "isbackupgeometry.init_notfound",
                new { model = quilt.Model.Name, id = quilt.Id, rc = st });
            return null;
        }
        st = (int)G.ProQuiltIsBackupgeometry(pQuilt, out var flag);
        if (st < 0)
        {
            CreoSdkLog.Trace("quilt", "isbackupgeometry.query_failed",
                new { model = quilt.Model.Name, id = quilt.Id, rc = st });
            return null;
        }
        Ensure(nameof(G.ProQuiltIsBackupgeometry), st);
        return flag != GNS.ProBooleans.PRO_B_FALSE;
    }

    // ---- Layer (DHandle 系,持久 id) ----

    /// <summary>枚举模型中所有 curve(遍历所有 feature + <c>ProFeatureGeomitemVisit(feat, PRO_CURVE, ...)</c>);
    /// datum curve 通常挂在 feature 内,通过 feature 逐一枚举收 curve id 去重。
    /// 非 Solid / owner 不可解析 → 空列表。同一 curve 可能被多 feature 引用 → 按 record struct 值语义去重。</summary>
    public IReadOnlyList<ItemRef> SolidCurveList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out _)) return Array.Empty<ItemRef>();
        var features = FeatureList(model);
        if (features.Count == 0) return Array.Empty<ItemRef>();
        var curves = new HashSet<ItemRef>();
        foreach (var feat in features)
        {
            var perFeat = FeatureGeomitemsList(feat, CreoModelItemType.Curve);
            foreach (var c in perFeat) curves.Add(c);
        }
        return curves.ToArray();
    }

    // ---- Axis (LIVE handle 系,DHandle 衍生;返 ProGeomitemdata union) ----

    /// <summary>ProAxisInit + ProAxisDataGet(返 ProGeomitemdata**)+ 解 union ProCurvedata line
    /// 抽 end1[3]/end2[3];finally ProGeomitemdataFree 释放。
    /// 头文件契约:obj_type = PRO_CURVE 且 curve type = PRO_LINE 才有 end1/end2。
    /// 非 line / owner 不可解析 → null。</summary>
    public AxisLineData? AxisLineDataGet(ItemRef axis)
    {
        if (!TryResolveMdl(axis.Model, out var mdl))
        {
            CreoSdkLog.Trace("axis", "linedataget.owner_unresolved",
                new { model = axis.Model.Name, id = axis.Id });
            return null;
        }
        int initSt = (int)G.ProAxisInit(mdl, axis.Id, out var pAxis);
        if (initSt < 0)
        {
            CreoSdkLog.Trace("axis", "linedataget.init_notfound",
                new { model = axis.Model.Name, id = axis.Id, rc = initSt });
            return null;
        }
        IntPtr pGeomData = IntPtr.Zero;
        int getSt = (int)G.ProAxisDataGet(pAxis, out pGeomData);
        if (getSt < 0 || pGeomData == IntPtr.Zero)
        {
            CreoSdkLog.Trace("axis", "linedataget.data_failed",
                new { model = axis.Model.Name, id = axis.Id, rc = getSt });
            return null;
        }
        try
        {
            // ProGeomitemdata = { int obj_type; union { ProCurvedata*, ProSurfacedata*, ProCsysdata* } data; }
            // 布局:第一个字段 obj_type(int, 4B),然后 padding 到指针对齐,然后 union ptr。
            int objType = Marshal.ReadInt32(pGeomData);
            IntPtr pCurveData = Marshal.ReadIntPtr(pGeomData, IntPtr.Size); // union ptr 在指针对齐偏移
            if (objType != (int)CreoModelItemType.Curve || pCurveData == IntPtr.Zero)
            {
                CreoSdkLog.Trace("axis", "linedataget.not_curve",
                    new { model = axis.Model.Name, id = axis.Id, objType });
                return null;
            }
            // ProCurvedata union:第一个字段 int type(=PRO_ENT_LINE=1),其余按 curve type 布局
            var line = Marshal.PtrToStructure<GNS.ptc_line>(pCurveData);
            if ((int)line.type != 1) // PRO_ENT_LINE = 1
            {
                CreoSdkLog.Trace("axis", "linedataget.not_line",
                    new { model = axis.Model.Name, id = axis.Id, curveType = (int)line.type });
                return null;
            }
            // ptc_line 里 end1/end2 是 fixed double[3];用 unsafe 复制出来
            double[] end1 = new double[3];
            double[] end2 = new double[3];
            unsafe
            {
                end1[0] = line.end1[0]; end1[1] = line.end1[1]; end1[2] = line.end1[2];
                end2[0] = line.end2[0]; end2[1] = line.end2[1]; end2[2] = line.end2[2];
            }
            return new AxisLineData(end1, end2);
        }
        finally
        {
            _ = G.ProGeomitemdataFree(ref pGeomData);
        }
    }

    /// <summary>ProAxisInit + ProAxisSurfaceGet(owner_mdl, pAxis, out pSurface)+ ProSurfaceIdGet 反查 id → ItemRef(model, Surface, id)。
    /// 无关联 owner surface / init 失败 → null。BORROWED handle 不 free。</summary>
    public ItemRef? AxisOwnerSurfaceGet(ItemRef axis)
    {
        if (!TryResolveMdl(axis.Model, out var mdl))
        {
            CreoSdkLog.Trace("axis", "ownersurfaceget.owner_unresolved",
                new { model = axis.Model.Name, id = axis.Id });
            return null;
        }
        int initSt = (int)G.ProAxisInit(mdl, axis.Id, out var pAxis);
        if (initSt < 0)
        {
            CreoSdkLog.Trace("axis", "ownersurfaceget.init_notfound",
                new { model = axis.Model.Name, id = axis.Id, rc = initSt });
            return null;
        }
        int surfSt = (int)G.ProAxisSurfaceGet(mdl, pAxis, out var pSurface);
        if (surfSt < 0 || pSurface == IntPtr.Zero)
        {
            // NOT_FOUND = 无关联 owner surface(自由 datum axis 等场景)
            CreoSdkLog.Trace("axis", "ownersurfaceget.no_owner",
                new { model = axis.Model.Name, id = axis.Id, rc = surfSt });
            return null;
        }
        if ((int)G.ProSurfaceIdGet(pSurface, out var surfaceId) != 0)
        {
            CreoSdkLog.Trace("axis", "ownersurfaceget.id_failed",
                new { model = axis.Model.Name, id = axis.Id });
            return null;
        }
        return new ItemRef(axis.Model, CreoModelItemType.Surface, surfaceId);
    }

    /// <summary>ProSurfaceInit + ProSurfaceContourVisit + ProContourEdgeVisit + ProEdgeIdGet:
    /// 嵌套 visit 枚举 surface 上所有 contour 的所有 edge,返 ItemRef(model, Edge, id) 去重列表。
    /// 同一 edge 可能出现在多个 contour(共享边缘),按 (model, type, id) 去重。
    /// 无 contour / 无 edge / owner 不可解析 → 空列表(query 语义)。</summary>
    public IReadOnlyList<ItemRef> SurfaceEdgeList(ItemRef surface)
    {
        if (!TryResolveMdl(surface.Model, out var mdl)) return Array.Empty<ItemRef>();
        int initSt = (int)G.ProSurfaceInit(mdl, surface.Id, out var pSurface);
        if (initSt < 0)
        {
            CreoSdkLog.Trace("surface", "edgelist.init_notfound",
                new { model = surface.Model.Name, id = surface.Id, rc = initSt });
            return Array.Empty<ItemRef>();
        }

        var edges = new List<ItemRef>();
        Exception? caught = null;

        // 内层 edge callback
        ProVisitAction edgeCb = (pEdge, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                if (filterStatus != GNS.ProErrors.PRO_TK_NO_ERROR) return GNS.ProErrors.PRO_TK_NO_ERROR;
                if ((int)G.ProEdgeIdGet(pEdge, out var edgeId) != 0) return GNS.ProErrors.PRO_TK_NO_ERROR;
                edges.Add(new ItemRef(surface.Model, CreoModelItemType.Edge, edgeId));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };
        var edgeCbPtr = _visitRegistry.Register(edgeCb);

        // 外层 contour callback:内部再触发 ProContourEdgeVisit
        ProVisitAction contourCb = (pContour, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                if (filterStatus != GNS.ProErrors.PRO_TK_NO_ERROR) return GNS.ProErrors.PRO_TK_NO_ERROR;
                var rcEdge = G.ProContourEdgeVisit(pSurface, pContour, edgeCbPtr, IntPtr.Zero, IntPtr.Zero);
                if (rcEdge != GNS.ProErrors.PRO_TK_NO_ERROR
                    && rcEdge != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                    && rcEdge != GNS.ProErrors.PRO_TK_NOT_EXIST)
                {
                    Check.Eval(nameof(G.ProContourEdgeVisit), rcEdge);
                }
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };
        var contourCbPtr = _visitRegistry.Register(contourCb);

        try
        {
            var err = G.ProSurfaceContourVisit(pSurface, contourCbPtr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProSurfaceContourVisit), err);
            }
        }
        finally
        {
            _visitRegistry.Unregister(contourCb);
            _visitRegistry.Unregister(edgeCb);
        }

        if (caught != null) throw caught;
        // 同一 edge 可能跨 contour;record struct 值语义去重
        return edges.Distinct().ToArray();
    }

    /// <summary>ProMdlLayerVisit + ProModelitemIdGet:枚举 model 所有 layer 并返 ItemRef(model, Layer, id) 列表。
    /// 无 layer 或不可读返空列表(query 语义)。visit 内 id 取失败跳过该项。</summary>
    public IReadOnlyList<ItemRef> ModelLayerList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var items = new List<ItemRef>();
        Exception? caught = null;

        ProVisitAction cb = (item, _, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                items.Add(new ItemRef(model, CreoModelItemType.Layer, mi.id));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProMdlLayerVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProMdlLayerVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return items;
    }

    /// <summary>ProLayerItemsPopulate:枚举 layer 上所有 modelitem;
    /// 出参 ProArray of <c>ProLayerItem</c> 由 <c>ProLayeritemarrayFree</c> 释放(finally)。
    /// 无 item / not_found / bad_inputs 归空列表(query 语义)。</summary>
    public IReadOnlyList<ItemRef> LayerItemsList(ItemRef layer)
    {
        if (!TryResolveMdl(layer.Model, out var mdl)) return Array.Empty<ItemRef>();

        var layerMi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)CreoModelItemType.Layer,
            id = layer.Id,
            owner = mdl,
        };

        IntPtr p = IntPtr.Zero;
        var rc = G.ProLayerItemsPopulate(ref layerMi, out p, out int count);
        if (IsNotFound(rc) || p == IntPtr.Zero || count == 0)
        {
            // NOT_FOUND / 空数组:layer 无 item,归空列表(不抛)
            if (p != IntPtr.Zero) _ = G.ProLayeritemarrayFree(ref p);
            return Array.Empty<ItemRef>();
        }
        try
        {
            Ensure(nameof(G.ProLayerItemsPopulate), (int)rc);
            var items = new List<ItemRef>(count);
            int elemSize = Marshal.SizeOf<GNS.ProLayerItem>();
            for (int i = 0; i < count; i++)
            {
                var elem = Marshal.PtrToStructure<GNS.ProLayerItem>(p + i * elemSize);
                int rawType = (int)elem.type;
                var itemType = Enum.IsDefined(typeof(CreoModelItemType), rawType)
                    ? (CreoModelItemType)rawType
                    : CreoModelItemType.Unknown;
                items.Add(new ItemRef(layer.Model, itemType, elem.id));
            }
            return items;
        }
        finally
        {
            _ = G.ProLayeritemarrayFree(ref p);
        }
    }
}
