using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- Drawing 独有 API ----

    /// <summary>resolve + ProDrawingSheetsCount;无法解析 → null。</summary>
    public int? DrawingSheetsCount(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        var rc = G.ProDrawingSheetsCount(mdl, out var count);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProDrawingSheetsCount), rc);
        return count;
    }

    /// <summary>ProDrawingCurrentSheetGet: 直绑取当前图纸页; st&lt;0 返 null。</summary>
    public int? DrawingCurrentSheet(ModelIdentity model)
    {
        // ProDrawingCurrentSheetGet 直绑
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProDrawingCurrentSheetGet(mdl, out var sheet);
        if (st < 0) return null;
        Ensure(nameof(G.ProDrawingCurrentSheetGet), st);
        return sheet;
    }

    // G.ProDrawingViewVisit 直绑。
    // ProDrawingViewVisit callback: 4 参数（drawing, view, filter_status, appData）
    public int? DrawingViewsCount(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;
        int count = 0;
        Exception? caught = null;

        ProDrawingViewVisitAction cb = (drawing, view, filterStatus, _) =>
        {
            try { count++; }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProDrawingViewVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                return null; // 不支持的类型等
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return count;
    }

    // 在 DrawingViewsCount 之上补 list 版,收 view_id 列表(不是 ItemRef,
    // 因为 ProDrawingViewVisit callback 给的是 view 句柄 IntPtr,不是 pro_model_item)。
    // 用 ProDrawingViewIdGet 从 view 句柄提取整型 id。
    public IReadOnlyList<int> DrawingViewIdList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<int>();
        var ids = new List<int>();
        Exception? caught = null;

        ProDrawingViewVisitAction cb = (drawing, view, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                if (G.ProDrawingViewIdGet(drawing, view, out var viewId) == GNS.ProErrors.PRO_TK_NO_ERROR)
                {
                    ids.Add(viewId);
                }
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProDrawingViewVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                return Array.Empty<int>(); // 不支持的类型等
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return ids;
    }

    /// <summary>遍历 view 并收集完整元数据(sheet/scale/name/solid)。
    /// 单个 view 的 sub-Get 失败时跳过该 view 并计入 skipped。</summary>
    public (IReadOnlyList<CreoDrawingView> Views, int Skipped) DrawingViewsList(
        ModelIdentity model, long sessionEpoch)
    {
        if (!TryResolveMdl(model, out var mdl))
            return (Array.Empty<CreoDrawingView>(), 0);

        var views = new List<CreoDrawingView>();
        int skipped = 0;
        Exception? caught = null;

        ProDrawingViewVisitAction cb = (drawing, view, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                if (G.ProDrawingViewIdGet(drawing, view, out var viewId) != GNS.ProErrors.PRO_TK_NO_ERROR)
                { skipped++; return GNS.ProErrors.PRO_TK_NO_ERROR; }

                if (G.ProDrawingViewSheetGet(drawing, view, out var sheet) != GNS.ProErrors.PRO_TK_NO_ERROR)
                { skipped++; return GNS.ProErrors.PRO_TK_NO_ERROR; }

                if (G.ProDrawingViewScaleGet(drawing, view, out var scale) != GNS.ProErrors.PRO_TK_NO_ERROR)
                { skipped++; return GNS.ProErrors.PRO_TK_NO_ERROR; }

                var nameBuf = new char[32];
                if (G.ProDrawingViewNameGet(drawing, view, nameBuf) != GNS.ProErrors.PRO_TK_NO_ERROR)
                { skipped++; return GNS.ProErrors.PRO_TK_NO_ERROR; }
                var nul = Array.IndexOf(nameBuf, '\0');
                var name = nul >= 0 ? new string(nameBuf, 0, nul) : new string(nameBuf);

                // solid 可选：view 可能无关联实体
                string? solidName = null;
                CreoModelType? solidType = null;
                if (G.ProDrawingViewSolidGet(drawing, view, out var solid) == GNS.ProErrors.PRO_TK_NO_ERROR
                    && solid != IntPtr.Zero)
                {
                    var solidNameBuf = new char[84];
                    if (G.ProMdlMdlnameGet(solid, solidNameBuf) == GNS.ProErrors.PRO_TK_NO_ERROR)
                    {
                        var sn = Array.IndexOf(solidNameBuf, '\0');
                        solidName = sn >= 0 ? new string(solidNameBuf, 0, sn) : new string(solidNameBuf);
                    }
                    if (G.ProMdlTypeGet(solid, out var mdlType) == GNS.ProErrors.PRO_TK_NO_ERROR)
                        solidType = FromProMdlType((int)mdlType);
                }

                views.Add(new CreoDrawingView(viewId, sheet, scale, name, solidName, solidType, sessionEpoch));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = G.ProDrawingViewVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                return (Array.Empty<CreoDrawingView>(), 0);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return (views, skipped);
    }

    /// <summary>ProDrawingSolidsVisit：收集 drawing 关联 solid，并立即复制为 ModelIdentity。</summary>
    public IReadOnlyList<ModelIdentity> DrawingSolidsList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ModelIdentity>();
        var solids = new List<ModelIdentity>();
        Exception? caught = null;

        ProVisitAction cb = (solid, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var name = new char[180];
                Check.Eval(nameof(G.ProMdlMdlnameGet), G.ProMdlMdlnameGet(solid, name));
                Check.Eval(nameof(G.ProMdlTypeGet), G.ProMdlTypeGet(solid, out var type));
                solids.Add(new ModelIdentity(FromProName(name), FromProMdlType((int)type)));
            }
            catch (Exception ex)
            {
                caught = ex;
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var rc = G.ProDrawingSolidsVisit(mdl, ptr, IntPtr.Zero, IntPtr.Zero);
            if (!IsNotFound(rc)) Check.Eval(nameof(G.ProDrawingSolidsVisit), rc);
        }
        finally
        {
            _visitRegistry.Unregister(cb);
        }

        if (caught != null) throw caught;
        return solids;
    }

    /// <summary>ProDrawingDtlsyminstVisit：收集指定 sheet 的 PRO_SYMBOL_INSTANCE model items。</summary>
    public IReadOnlyList<ItemRef> DrawingDetailSymbolInstancesList(ModelIdentity model, int sheet)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var items = new List<ItemRef>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var nativeItem = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                items.Add(new ItemRef(model, CreoModelItemType.SymbolInstance, nativeItem.id));
            }
            catch (Exception ex)
            {
                caught = ex;
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var rc = G.ProDrawingDtlsyminstVisit(mdl, sheet, ptr, IntPtr.Zero, IntPtr.Zero);
            if (!IsNotFound(rc)) Check.Eval(nameof(G.ProDrawingDtlsyminstVisit), rc);
        }
        finally
        {
            _visitRegistry.Unregister(cb);
        }

        if (caught != null) throw caught;
        return items;
    }

    // ---- Drawing 新增 bridge ----

    public ModelIdentity? DrawingCurrentSolid(ModelIdentity model)
    {
        // ProDrawingCurrentsolidGet 直绑 → IntPtr solid + ProMdlMdlnameGet + ProMdlTypeGet
        if (!TryResolveMdl(new ItemRef(model, CreoModelItemType.Unknown, 0), out var mdl)) return null;
        int st = (int)G.ProDrawingCurrentsolidGet(mdl, out var solid);
        if (st != 0) return null;   // PRO_TK_E_NOT_FOUND = 无关联实体
        var nameBuf = new char[84];
        if ((int)G.ProMdlMdlnameGet(solid, nameBuf) != 0) return null;
        int nameLen = Array.IndexOf(nameBuf, '\0');
        string solidName = nameLen < 0 ? new string(nameBuf) : new string(nameBuf, 0, nameLen);
        if ((int)G.ProMdlTypeGet(solid, out var mdlType) != 0) return null;
        return new ModelIdentity(solidName, FromProMdlType((int)mdlType));
    }

    // ---- Drawing Visitor bridge ----

    // G.ProDrawingTableVisit 直绑。
    // ProDwgtable ≡ pro_model_item，PtrToStructure 读 type/id。
    public IReadOnlyList<ItemRef> DrawingTableList(ModelIdentity model)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        return VisitCollect<ItemRef>(G.ProDrawingTableVisit, mdl, (item, _) =>
        {
            var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
            return new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id);
        });
    }

    // G.ProDrawingDimensionVisit 直绑。
    // dimType=0: 两次循环收 PRO_DIMENSION(8) + PRO_REF_DIMENSION(28)；其他值直接用。
    // (pro_obj_types 实际值见 ProObjects.Enums.g.cs;不是 11/13)
    // ProDimension ≡ pro_model_item，PtrToStructure 读 type/id。
    public IReadOnlyList<ItemRef> DrawingDimensionList(ModelIdentity model, int dimType)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var items = new List<ItemRef>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                items.Add(new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            if (dimType == 0)
            {
                // 收 PRO_DIMENSION(8) + PRO_REF_DIMENSION(28) 两类(pro_obj_types 真值)
                foreach (var t in new[] { 8, 28 }) // PRO_DIMENSION=8, PRO_REF_DIMENSION=28
                {
                    var e = G.ProDrawingDimensionVisit(mdl, (GNS.pro_obj_types)t, ptr, IntPtr.Zero, IntPtr.Zero);
                    if (e != GNS.ProErrors.PRO_TK_NO_ERROR
                        && e != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                        && e != GNS.ProErrors.PRO_TK_NOT_EXIST)
                    {
                        if (caught != null) throw caught;
                        Check.Eval(nameof(G.ProDrawingDimensionVisit), e);
                    }
                    if (caught != null) throw caught;
                }
            }
            else
            {
                var e = G.ProDrawingDimensionVisit(mdl, (GNS.pro_obj_types)dimType, ptr, IntPtr.Zero, IntPtr.Zero);
                if (e != GNS.ProErrors.PRO_TK_NO_ERROR
                    && e != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                    && e != GNS.ProErrors.PRO_TK_NOT_EXIST)
                {
                    if (caught != null) throw caught;
                    Check.Eval(nameof(G.ProDrawingDimensionVisit), e);
                }
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return items;
    }

    /// <summary>ProDrawingDtlgroupsCollect 直绑:枚举 drawing/sheet 里的 draft groups(dtlgroup)。
    /// <para>NotFound/NotExist → 空列表; OOM 走 Check.Eval 透传 rc 抛;
    /// 返 items type 固定 PRO_DRAFT_GROUP(83) → CreoModelItemType.DraftGroup。</para></summary>
    public IReadOnlyList<ItemRef> DrawingDtlgroupsList(ModelIdentity model, int sheet)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();

        var rc = G.ProDrawingDtlgroupsCollect(mdl, sheet, out var arr);
        if (IsNotFound(rc)) return Array.Empty<ItemRef>();
        Check.Eval(nameof(G.ProDrawingDtlgroupsCollect), rc);
        using var scope = ProArrayScope.OwnedPlain(arr);
        if (scope.Handle == IntPtr.Zero) return Array.Empty<ItemRef>();

        var items = ProArrayMarshal.ReadStructs<GNS.pro_model_item>(scope.Handle);
        if (items.Length == 0) return Array.Empty<ItemRef>();

        var result = new List<ItemRef>(items.Length);
        foreach (var mi in items)
            result.Add(new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id));
        CreoSdkLog.Trace("drawing", "dtlgroup-list.bridge",
            new { drawing = model.Name, sheet, count = result.Count });
        return result;
    }

    // ---- pt_drawing draft entity bridge 真绑 ----

    /// <summary>ProDrawingDtlentitiesCollect 直绑。
    /// <para>OOM 显式抛 <see cref="CreoException"/>(OutOfMemory)，不映射 NotFound。</para>
    /// <para>NotFound/NotExist → 返空列表; 其他 rc 经 Check.Eval 透传。</para>
    /// <para>经 ProArrayScope 配对释放。</para></summary>
    public IReadOnlyList<CreoDtlentity> DrawingDtlentitiesList(ModelIdentity drawing, int sheet, long sessionEpoch)
    {
        if (!TryResolveMdl(drawing, out var mdl))
        {
            CreoSdkLog.Trace("drawing", "dtlentity-list.owner_unresolved",
                new { drawing = drawing.Name });
            return Array.Empty<CreoDtlentity>();
        }

        var symbol = default(GNS.pro_model_item);
        var arr = IntPtr.Zero;
        var rc = G.ProDrawingDtlentitiesCollect(mdl, ref symbol, sheet, out arr);

        // OOM 显式抛, 不映射 NotFound, 不静默吞
        if (rc == GNS.ProErrors.PRO_TK_OUT_OF_MEMORY)
            throw new CreoException("ProDrawingDtlentitiesCollect", ProError.OutOfMemory);

        if (IsNotFound(rc))
            return Array.Empty<CreoDtlentity>();

        Check.Eval(nameof(G.ProDrawingDtlentitiesCollect), rc);
        using var scope = ProArrayScope.OwnedPlain(arr);
        if (scope.Handle == IntPtr.Zero)
            return Array.Empty<CreoDtlentity>();

        // 遍历 ProArray (pro_model_item POD 数组) 翻 CreoDtlentity, 注入 sessionEpoch;
        // SizeGet 容错(失败/空 → 空列表)与原语义一致。
        var items = ProArrayMarshal.ReadStructs<GNS.pro_model_item>(scope.Handle);
        if (items.Length == 0)
            return Array.Empty<CreoDtlentity>();

        var result = new List<CreoDtlentity>(items.Length);
        foreach (var mi in items)
        {
            result.Add(new CreoDtlentity(
                drawing.Name, drawing.Type,
                (CreoModelItemType)(int)mi.type,
                mi.id, sessionEpoch));
        }
        CreoSdkLog.Trace("drawing", "dtlentity-list.bridge",
            new { drawing = drawing.Name, sheet, sessionEpoch, count = result.Count });
        return result;
    }

    /// <summary>ProDtlentityDataGet → ProDtlentitydata 直绑 + 3 sub-Get 串调。
    /// <para>owner unresolved / entity_notfound / subget_failed 各路径 → return null + trace reason 区分。</para>
    /// <para>ProDtlentitydataFree 经 try/finally 配对释放。</para>
    /// <para>FontGet caller buffer char[32], 找 '\0' 截断; 无 null → trace font_truncated。</para>
    /// <para>ColorGet native pro_color → SDK CreoColor 映射: method 分流 (Default / Type / Rgb)。</para></summary>
    public CreoDtlentityData? DrawingDtlentityDataGet(ModelIdentity drawing, CreoDtlentity entity)
    {
        if (!TryResolveMdl(drawing, out var mdl))
        {
            CreoSdkLog.Trace("drawing", "dtlentity-data-get.owner_unresolved",
                new { drawing = drawing.Name, entityId = entity.Id });
            return null;
        }

        // 重建 pro_model_item: type=PRO_DRAFT_ENTITY (真值 77, 见 ProObjects.Enums.g.cs:50)
        var nativeEntity = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_DRAFT_ENTITY,
            id = entity.Id,
            owner = mdl,
        };
        var symbol = default(GNS.pro_model_item);

        var entdata = IntPtr.Zero;
        var rc = G.ProDtlentityDataGet(ref nativeEntity, ref symbol, out entdata);
        // "entity 不存在"的真机形态是 GENERAL_ERROR:头文件返回值只有
        // NO_ERROR/BAD_INPUTS/GENERAL_ERROR(ProDtlentity.h:66-69,无 E_NOT_FOUND),
        // 真机实证不存在 id → GENERAL_ERROR(2026-07-07 probe)。GENERAL_ERROR 与
        // IsNotFound 一律归 null(读不到契约);BAD_INPUTS(编程错误)仍抛。
        if (IsNotFound(rc) || rc == GNS.ProErrors.PRO_TK_GENERAL_ERROR)
        {
            CreoSdkLog.Trace("drawing", "dtlentity-data-get.entity_notfound",
                new { drawing = drawing.Name, entityId = entity.Id, rc = (int)rc });
            return null;
        }
        Check.Eval(nameof(G.ProDtlentityDataGet), rc);

        try
        {
            // 3 sub-Get 串调 (Color / Font / Width) - 任一失败 → trace subget_failed + return null
            // event 用 . 分隔语义键, reason 走 props { which = ... }
            var color = default(GNS.pro_color);
            var colorRc = G.ProDtlentitydataColorGet(entdata, ref color);
            if (colorRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-data-get.subget_failed",
                    new { drawing = drawing.Name, entityId = entity.Id, which = "color", rc = (int)colorRc });
                return null;
            }

            var fontBuf = new char[32];
            var fontRc = G.ProDtlentitydataFontGet(entdata, fontBuf);
            if (fontRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-data-get.subget_failed",
                    new { drawing = drawing.Name, entityId = entity.Id, which = "font", rc = (int)fontRc });
                return null;
            }
            var nul = Array.IndexOf(fontBuf, '\0');
            var fontName = nul >= 0 ? new string(fontBuf, 0, nul) : new string(fontBuf);
            if (nul < 0)
            {
                // 无 '\0' 表示 ≥ 32 UTF-16 code units, Toolkit 固定缓冲截断
                CreoSdkLog.Trace("drawing", "dtlentity-data-get.font_truncated",
                    new { drawing = drawing.Name, entityId = entity.Id, len = 32 });
            }

            var widthRc = G.ProDtlentitydataWidthGet(entdata, out var lineWidth);
            if (widthRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-data-get.subget_failed",
                    new { drawing = drawing.Name, entityId = entity.Id, which = "width", rc = (int)widthRc });
                return null;
            }

            // pro_color → CreoColor 映射 (method 分流)
            // 未匹配 method → throw NotSupportedException (fail-loud, 不静默退回 Default)
            // ProColortype 反向 cast 加 Enum.IsDefined 验防漂值 (silent drift)
            CreoColor sdkColor;
            switch (color.method)
            {
                case GNS.pro_color_method.PRO_COLOR_METHOD_DEFAULT:
                    sdkColor = CreoColor.Default;
                    break;
                case GNS.pro_color_method.PRO_COLOR_METHOD_TYPE:
                    var nativeTypeValue = (int)color.value.type;
                    if (!Enum.IsDefined(typeof(CreoColorType), nativeTypeValue))
                    {
                        CreoSdkLog.Warn(
                            "drawing", "dtlentity-data-get.unsupported_color_type",
                            "ProColortype 漂出未在 CreoColorType 定义的新值(Creo 9/11 升级 SDK 同步漂移检测点)",
                            new { drawing = drawing.Name, entityId = entity.Id, nativeTypeValue });
                        throw new NotSupportedException(
                            $"ProColortype value {nativeTypeValue} 未定义在 CreoColorType (Creo 漂值, SDK enum 需同步升级)。");
                    }
                    sdkColor = CreoColor.FromType((CreoColorType)nativeTypeValue);
                    break;
                case GNS.pro_color_method.PRO_COLOR_METHOD_RGB:
                    sdkColor = CreoColor.FromRgb(color.value.map.red, color.value.map.green, color.value.map.blue);
                    break;
                default:
                    CreoSdkLog.Warn(
                        "drawing", "dtlentity-data-get.unsupported_color_method",
                        "pro_color_method 漂出未支持值(CreoColorMethod SDK enum 需同步升级)",
                        new { drawing = drawing.Name, entityId = entity.Id, method = (int)color.method });
                    throw new NotSupportedException(
                        $"pro_color_method {(int)color.method} 未支持 (Creo 漂值, CreoColorMethod 需同步升级)。");
            }

            // 4th sub-Get: curve 几何
            // ProDtlentitydataCurveGet → caller-alloc ptc_curve union;
            // 按 ProCurvedataTypeGet 分派读取。
            //
            // 内存契约(三方佐证: ProCurvedata.h 原文 + ProSplinedataGet 的
            // "Free this output using ProArrayFree()" 指引 + PTC 示例 ProCurveToNURBS→
            // ProCurveDataFree 配对形态; VB API/J-Link 为 GC/COM 自动管理不提供 C 级反证):
            //   1. union 壳 = C# AllocHGlobal → 只经 Marshal.FreeHGlobal(分配器配对不变量),
            //      禁 ProCurvedataFree("underlying memory",与 ProCurvedataAlloc 配对)。
            //   2. union 内部动态成员(spline/bspline/polygon 等由 Creo 填充) →
            //      ProCurvedataMemoryFree("frees the top-level memory used by the structure",
            //      头文件无类型限制)。CurveGet 成功后所有路径(含 TypeGet 失败/
            //      marshal 失败/未处理类型)都必须走此释放,提升到本 finally。
            //   3. ProSplinedataGet/ProBsplinedataGet 输出数组是函数按需分配的 ProArray 副本
            //      ("ignores the output arguments with null pointers" + ProArrayFree 指引),
            //      在 MarshalCurveData 内逐个 ProArrayFree。
            CreoDtlentityCurve? curveData = null;
            var curveSize = Marshal.SizeOf<GNS.ptc_curve>();
            var curvePtr = Marshal.AllocHGlobal(curveSize);
            var curveGetOk = false;
            try
            {
                // 清零 caller-alloc 缓冲
                unsafe { new Span<byte>((void*)curvePtr, curveSize).Clear(); }

                var curveRc = G.ProDtlentitydataCurveGet(entdata, curvePtr);
                curveGetOk = curveRc == GNS.ProErrors.PRO_TK_NO_ERROR;
                if (curveGetOk)
                {
                    var typeRc = G.ProCurvedataTypeGet(curvePtr, out var curveType);
                    if (typeRc == GNS.ProErrors.PRO_TK_NO_ERROR)
                    {
                        curveData = MarshalCurveData(curvePtr, curveType, drawing.Name, entity.Id);
                    }
                    else
                    {
                        CreoSdkLog.Trace("drawing", "dtlentity-data-get.subget_failed",
                            new { drawing = drawing.Name, entityId = entity.Id, which = "curve_type", rc = (int)typeRc });
                    }
                }
                else if (curveRc != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                      && curveRc != GNS.ProErrors.PRO_TK_NOT_EXIST)
                {
                    CreoSdkLog.Trace("drawing", "dtlentity-data-get.subget_failed",
                        new { drawing = drawing.Name, entityId = entity.Id, which = "curve", rc = (int)curveRc });
                }
            }
            finally
            {
                // 释放顺序: 先 Creo 内部动态成员(仅 CurveGet 成功后才可能被填充),再 C# 壳
                if (curveGetOk)
                    G.ProCurvedataMemoryFree(curvePtr);
                Marshal.FreeHGlobal(curvePtr);
            }

            CreoSdkLog.Trace("drawing", "dtlentity-data-get.success",
                new {
                    drawing = drawing.Name,
                    entityId = entity.Id,
                    colorMethod = (int)color.method,
                    fontLen = nul >= 0 ? nul : 32,
                    lineWidth,
                    hasCurve = curveData.HasValue,
                    curveKind = curveData?.Kind.ToString(),
                });
            return new CreoDtlentityData(entity, sdkColor, fontName, lineWidth, curveData);
        }
        finally
        {
            if (entdata != IntPtr.Zero)
                G.ProDtlentitydataFree(entdata);
        }
    }

    /// <summary>按 curve type 分派从 caller-alloc ptc_curve union 读取几何字段,
    /// 组装 <see cref="CreoDtlentityCurve"/> DATA 快照。
    /// <para>union 内部动态内存的 ProCurvedataMemoryFree 由调用方
    /// (<see cref="DrawingDtlentityDataGet"/> 的 curve finally)统一执行,本方法不释放 union;
    /// 仅 ProSplinedataGet/ProBsplinedataGet 的函数分配 ProArray 副本在此逐个 ProArrayFree
    /// (头文件: "Free this output using ProArrayFree()")。</para>
    /// <para>任一 sub-Get 失败 → trace <c>curve_marshal_failed</c>(带 rc + curveType) + return null。</para></summary>
    private CreoDtlentityCurve? MarshalCurveData(
        IntPtr curvePtr, GNS.pro_ent_type curveType,
        string drawingName, int entityId)
    {
        var kind = (CreoDtlentityCurveKind)(int)curveType;
        switch (curveType)
        {
            case GNS.pro_ent_type.PRO_ENT_LINE:
            case GNS.pro_ent_type.PRO_ENT_ARROW:
            {
                // ptc_arrow 与 ptc_line 布局相同,共用 ProLinedataGet
                var e1 = new double[3]; var e2 = new double[3];
                var rc = G.ProLinedataGet(curvePtr, e1, e2);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                return new CreoDtlentityCurve(kind,
                    (e1[0], e1[1], e1[2]), (e2[0], e2[1], e2[2]),
                    null, null, null, null, null, null, null, null, 2, 0);
            }
            case GNS.pro_ent_type.PRO_ENT_ARC:
            {
                var v1 = new double[3]; var v2 = new double[3]; var orig = new double[3];
                var rc = G.ProArcdataGet(curvePtr, v1, v2, orig,
                    out var startAngle, out var endAngle, out var radius);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                return new CreoDtlentityCurve(kind,
                    null, null,
                    (orig[0], orig[1], orig[2]),
                    (v1[0], v1[1], v1[2]), (v2[0], v2[1], v2[2]),
                    startAngle, endAngle, radius,
                    null, null, 0, 0);
            }
            case GNS.pro_ent_type.PRO_ENT_CIRCLE:
            {
                // ptc_circle: center[3] + norm_axis_unit_vect[3] + radius
                // 没有独立 ProCircledataGet,直接 PtrToStructure
                var c = Marshal.PtrToStructure<GNS.ptc_circle>(curvePtr);
                unsafe
                {
                    return new CreoDtlentityCurve(kind,
                        null, null, null, null, null, null, null,
                        c.radius,
                        (c.center[0], c.center[1], c.center[2]),
                        (c.norm_axis_unit_vect[0], c.norm_axis_unit_vect[1], c.norm_axis_unit_vect[2]),
                        0, 0);
                }
            }
            case GNS.pro_ent_type.PRO_ENT_POINT:
            {
                var pos = new double[3];
                var rc = G.ProPointdataGet(curvePtr, pos);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                return new CreoDtlentityCurve(kind,
                    null, null, (pos[0], pos[1], pos[2]),
                    null, null, null, null, null, null, null, 1, 0);
            }
            case GNS.pro_ent_type.PRO_ENT_ELLIPSE:
            {
                var center = new double[3]; var xAxis = new double[3]; var normal = new double[3];
                var rc = G.ProEllipsedataGet(curvePtr, center, xAxis, normal,
                    out var xRadius, out var yRadius, out var startAng, out var endAng);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                return new CreoDtlentityCurve(kind,
                    null, null,
                    (center[0], center[1], center[2]),
                    (xAxis[0], xAxis[1], xAxis[2]),
                    (normal[0], normal[1], normal[2]),
                    startAng, endAng, xRadius,
                    null, null, 0, 0,
                    MinorRadius: yRadius);
            }
            case GNS.pro_ent_type.PRO_ENT_SPLINE:
            {
                var rc = G.ProSplinedataGet(curvePtr,
                    out var parArr, out var pntArr, out var tanArr, out var numPoints);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                try
                {
                    // 计数已取,点/切向数组本体留 future 扩展
                    return new CreoDtlentityCurve(kind,
                        null, null, null, null, null, null, null, null, null, null,
                        numPoints, 0);
                }
                finally
                {
                    // ProSplinedataGet 输出是函数分配的 ProArray 副本,逐个 ProArrayFree
                    if (parArr != IntPtr.Zero) G.ProArrayFree(ref parArr);
                    if (pntArr != IntPtr.Zero) G.ProArrayFree(ref pntArr);
                    if (tanArr != IntPtr.Zero) G.ProArrayFree(ref tanArr);
                }
            }
            case GNS.pro_ent_type.PRO_ENT_B_SPLINE:
            {
                var rc = G.ProBsplinedataGet(curvePtr,
                    out var degree, out var bParams, out var weights,
                    out var cPnts, out var numKnots, out var numCPoints);
                if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return TraceMarshalFailed(drawingName, entityId, curveType, rc);
                try
                {
                    return new CreoDtlentityCurve(kind,
                        null, null, null, null, null, null, null, null, null, null,
                        numKnots, numCPoints);
                }
                finally
                {
                    // ProBsplinedataGet 输出同为 ProArray 副本; weights 非有理时为 NULL
                    if (bParams != IntPtr.Zero) G.ProArrayFree(ref bParams);
                    if (weights != IntPtr.Zero) G.ProArrayFree(ref weights);
                    if (cPnts != IntPtr.Zero) G.ProArrayFree(ref cPnts);
                }
            }
            case GNS.pro_ent_type.PRO_ENT_TXT:
            {
                // ptc_text: 直接 PtrToStructure 读 location
                var t = Marshal.PtrToStructure<GNS.ptc_text>(curvePtr);
                unsafe
                {
                    return new CreoDtlentityCurve(kind,
                        null, null,
                        (t.location[0], t.location[1], t.location[2]),
                        null, null, null, null, null, null, null, 0, 0);
                }
            }
            default:
            {
                // 未显式处理的类型(Polygon/CompositeCurve/SurfaceCurve/ParamCurve):记 kind + 0 几何
                // (内部指针若有,由调用方 finally 的 ProCurvedataMemoryFree 统一释放)
                CreoSdkLog.Trace("drawing", "dtlentity-data-get.curve_type_unhandled",
                    new { drawingName, entityId, curveType = (int)curveType });
                return new CreoDtlentityCurve(kind,
                    null, null, null, null, null, null, null, null, null, null, 0, 0);
            }
        }
    }

    /// <summary>curve sub-Get 失败统一出口: trace curve_marshal_failed(带 rc + curveType)后返 null,
    /// 让 null 语义可诊断。</summary>
    private static CreoDtlentityCurve? TraceMarshalFailed(
        string drawingName, int entityId, GNS.pro_ent_type curveType, GNS.ProErrors rc)
    {
        CreoSdkLog.Trace("drawing", "dtlentity-data-get.curve_marshal_failed",
            new { drawingName, entityId, curveType = (int)curveType, rc = (int)rc });
        return null;
    }

    // ---- draft entity 创建方向 (fixture 程序化生成) ----

    /// <summary>在 drawing 上创建一条 draft entity (Create 方向,与 <see cref="DrawingDtlentityDataGet"/>
    /// 读回方向互为对偶验证)。
    /// <para>链路: caller-alloc ptc_curve → 按 Kind 调 ProXxxdataInit(Circle 无 Init 直填 struct;
    /// Spline/BSpline 经 ProArrayAlloc 构造输入数组) → ProDtlentitydataAlloc →
    /// CurveSet + ColorSet → ProDtlentityCreate(symbol=NULL, ProDtlentity.h: 加到 owner 本体传 NULL)。</para>
    /// <para>内存: union 壳 FreeHGlobal; entdata 经 ProDtlentitydataFree; Spline/BSpline 的
    /// 输入 ProArray 在 CurveSet(复制语义)后 ProArrayFree——内部指针是自建 ProArray 且已亲手释放,
    /// 因此本方法不对 union 调 ProCurvedataMemoryFree(避免双重释放)。</para>
    /// <para>返回创建的 entity id; 失败返 null + trace reason。</para></summary>
    public int? DrawingDtlentityCreate(ModelIdentity drawing, CreoDtlentityCurve curve, CreoColor color)
    {
        if (!TryResolveMdl(drawing, out var mdl))
        {
            CreoSdkLog.Trace("drawing", "dtlentity-create.owner_unresolved",
                new { drawing = drawing.Name, kind = curve.Kind.ToString() });
            return null;
        }

        var curveSize = Marshal.SizeOf<GNS.ptc_curve>();
        var curvePtr = Marshal.AllocHGlobal(curveSize);
        var entdata = IntPtr.Zero;
        // Spline/BSpline 的输入 ProArray 句柄(CurveSet 复制语义后释放),统一 Dispose 收敛
        var proArrays = new List<ProArrayScope>();
        try
        {
            unsafe { new Span<byte>((void*)curvePtr, curveSize).Clear(); }

            if (!InitCurveData(curvePtr, curve, proArrays, drawing.Name))
            {
                return null;
            }

            var allocRc = G.ProDtlentitydataAlloc(mdl, out entdata);
            if (allocRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.alloc_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)allocRc });
                return null;
            }

            var setRc = G.ProDtlentitydataCurveSet(entdata, curvePtr);
            if (setRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.curve_set_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)setRc });
                return null;
            }

            var nativeColor = ToNativeColor(color);
            var colorRc = G.ProDtlentitydataColorSet(entdata, ref nativeColor);
            if (colorRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.color_set_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)colorRc });
                return null;
            }

            // draft entity 必须关联 view (ProCurvedata.h "必设 view");
            // view-independent 实体绑到 background view(sheet 1)
            var bgViewRc = G.ProDrawingBackgroundViewGet(mdl, 1, out var bgView);
            if (bgViewRc != GNS.ProErrors.PRO_TK_NO_ERROR || bgView == IntPtr.Zero)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.bg_view_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)bgViewRc });
                return null;
            }
            var viewSetRc = G.ProDtlentitydataViewSet(entdata, bgView);
            if (viewSetRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.view_set_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)viewSetRc });
                return null;
            }

            var entity = default(GNS.pro_model_item);
            var createRc = GeneratedHelpers.ProDtlentityCreate(mdl, IntPtr.Zero, entdata, ref entity);
            if (createRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.create_failed",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), rc = (int)createRc });
                return null;
            }

            // 建→Draw 显示(官方样板序列;ProDtlentity.h 无 Show,entity 只有 Draw/Erase 临时对)。
            // Draw 失败不吞:warn 让显示失效可诊断。
            // 已知边界(2026-07-07 真机):init 上下文中 Create/Draw/Save 全 rc=0 且保存产物
            // 体积增长(实体真实落盘),但同上下文内一切读回路径全灭——Collect 空、
            // ProDtlentityDataGet 连出参 handle 都返 GENERAL_ERROR。读回须 post-init
            // 且 drawing 真打开显示。
            var drawRc = G.ProDtlentityDraw(ref entity);
            if (drawRc != GNS.ProErrors.PRO_TK_NO_ERROR)
            {
                CreoSdkLog.Warn("drawing", "dtlentity-create.draw_failed",
                    $"ProDtlentityDraw rc={(int)drawRc}: 实体 {entity.id} 已创建但未显示",
                    new { drawing = drawing.Name, kind = curve.Kind.ToString(), entityId = entity.id, rc = (int)drawRc });
            }

            CreoSdkLog.Trace("drawing", "dtlentity-create.success",
                new { drawing = drawing.Name, kind = curve.Kind.ToString(), entityId = entity.id, entityType = (int)entity.type });
            return entity.id;
        }
        finally
        {
            foreach (var scope in proArrays)
                scope.Dispose();
            if (entdata != IntPtr.Zero)
                G.ProDtlentitydataFree(entdata);
            Marshal.FreeHGlobal(curvePtr);
        }
    }

    /// <summary>按 Kind 初始化 caller-alloc ptc_curve union。失败 trace + 返 false。
    /// Spline/BSpline 构造的输入 ProArray 追加进 <paramref name="proArrays"/> 由调用方释放。</summary>
    private static bool InitCurveData(
        IntPtr curvePtr, CreoDtlentityCurve curve, List<ProArrayScope> proArrays, string drawingName)
    {
        GNS.ProErrors rc;
        switch (curve.Kind)
        {
            case CreoDtlentityCurveKind.Line:
            {
                var (e1, e2) = (RequireVec(curve.End1), RequireVec(curve.End2));
                rc = G.ProLinedataInit(e1, e2, curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.Arc:
            {
                rc = G.ProArcdataInit(
                    RequireVec(curve.Vector1), RequireVec(curve.Vector2), RequireVec(curve.Origin),
                    curve.StartAngle ?? 0, curve.EndAngle ?? 0, curve.Radius ?? 0, curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.Point:
            {
                rc = G.ProPointdataInit(RequireVec(curve.Origin), curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.Ellipse:
            {
                rc = G.ProEllipsedataInit(
                    RequireVec(curve.Origin), RequireVec(curve.Vector1), RequireVec(curve.Vector2),
                    curve.Radius ?? 0, curve.MinorRadius ?? 0,
                    curve.StartAngle ?? 0, curve.EndAngle ?? 0, curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.Circle:
            {
                // 无 ProCircledataInit;Creo 4 的 ProDtlentityCreate 不接受手工构造的 ptc_circle。
                // 改用 ProArcdataInit 创建 0→2PI 全圆弧(Arc 与 Circle 视觉等价,
                // 读回时 ProCurvedataTypeGet 仍返 PRO_ENT_ARC)。
                var center = RequireVec(curve.Center);
                var v1 = new double[] { 1, 0, 0 };
                var v2 = new double[] { 0, 1, 0 };
                rc = G.ProArcdataInit(v1, v2, center,
                    0, 2.0 * Math.PI, curve.Radius ?? 0, curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.Spline:
            {
                // 输入是 ProArray(头文件),经 ProArrayAlloc 构造: par=n doubles, pnt/tan=n*3 doubles。
                // 每次 alloc 后立即入列,消除连环 alloc 间托管异常导致的孤儿句柄窗口。
                var n = curve.NumPoints;
                var par = ProArrayMarshal.AllocDoubles(BuildSplineParams(n), 1);
                proArrays.Add(par);
                var pnt = ProArrayMarshal.AllocDoubles(BuildSplinePoints(n), 3);
                proArrays.Add(pnt);
                var tan = ProArrayMarshal.AllocDoubles(BuildSplineTangents(n), 3);
                proArrays.Add(tan);
                if (par.Handle == IntPtr.Zero || pnt.Handle == IntPtr.Zero || tan.Handle == IntPtr.Zero)
                {
                    rc = GNS.ProErrors.PRO_TK_OUT_OF_MEMORY;
                    break;
                }
                rc = GeneratedHelpers.ProSplinedataInit(par.Handle, pnt.Handle, tan.Handle, n, curvePtr);
                break;
            }
            case CreoDtlentityCurveKind.BSpline:
            {
                // degree=3, knots=NumPoints, c_pnts=NumControlPoints; weights=NULL(非有理)。
                // 每次 alloc 后立即入列,消除连环 alloc 间托管异常导致的孤儿句柄窗口。
                var numKnots = curve.NumPoints;
                var numCPts = curve.NumControlPoints;
                var knots = ProArrayMarshal.AllocDoubles(BuildBsplineKnots(numKnots), 1);
                proArrays.Add(knots);
                var cPnts = ProArrayMarshal.AllocDoubles(BuildBsplineControlPoints(numCPts), 3);
                proArrays.Add(cPnts);
                if (knots.Handle == IntPtr.Zero || cPnts.Handle == IntPtr.Zero)
                {
                    rc = GNS.ProErrors.PRO_TK_OUT_OF_MEMORY;
                    break;
                }
                rc = GeneratedHelpers.ProBsplinedataInit(
                    3, knots.Handle, IntPtr.Zero, cPnts.Handle, numKnots, numCPts, curvePtr);
                break;
            }
            default:
            {
                CreoSdkLog.Trace("drawing", "dtlentity-create.kind_unsupported",
                    new { drawingName, kind = curve.Kind.ToString() });
                return false;
            }
        }

        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Trace("drawing", "dtlentity-create.curve_init_failed",
                new { drawingName, kind = curve.Kind.ToString(), rc = (int)rc });
            return false;
        }
        return true;
    }

    /// <summary>nullable 三元组转 double[3];Create 方向字段缺失是编程错误,直接抛。</summary>
    private static double[] RequireVec((double X, double Y, double Z)? v)
    {
        if (v is null)
            throw new ArgumentException("curve 几何字段缺失,无法初始化 ProCurvedata(见 CreoDtlentityCurve 的 Kind→字段映射表)。");
        return new[] { v.Value.X, v.Value.Y, v.Value.Z };
    }

    // fixture spline/bspline 的确定性输入数据(golden 真源在 sample 常量,此处形态仅需合法)
    private static double[] BuildSplineParams(int n)
    {
        var par = new double[n];
        for (int i = 0; i < n; i++) par[i] = i / (double)(n - 1);
        return par;
    }

    private static double[] BuildSplinePoints(int n)
    {
        // 平面正弦形态插值点(z=0)
        var pts = new double[n * 3];
        for (int i = 0; i < n; i++)
        {
            pts[i * 3 + 0] = 300 + i * 20;
            pts[i * 3 + 1] = 400 + 30 * Math.Sin(i * Math.PI / 2);
            pts[i * 3 + 2] = 0;
        }
        return pts;
    }

    private static double[] BuildSplineTangents(int n)
    {
        var tans = new double[n * 3];
        for (int i = 0; i < n; i++)
        {
            tans[i * 3 + 0] = 1;
            tans[i * 3 + 1] = 0;
            tans[i * 3 + 2] = 0;
        }
        return tans;
    }

    private static double[] BuildBsplineKnots(int numKnots)
    {
        // clamped uniform knot 向量(degree=3): 首尾各 4 重
        var knots = new double[numKnots];
        for (int i = 0; i < numKnots; i++)
        {
            if (i < 4) knots[i] = 0;
            else if (i >= numKnots - 4) knots[i] = 1;
            else knots[i] = (i - 3) / (double)(numKnots - 7);
        }
        return knots;
    }

    private static double[] BuildBsplineControlPoints(int numCPts)
    {
        var pts = new double[numCPts * 3];
        for (int i = 0; i < numCPts; i++)
        {
            pts[i * 3 + 0] = 500 + i * 25;
            pts[i * 3 + 1] = 200 + (i % 2 == 0 ? 0 : 50);
            pts[i * 3 + 2] = 0;
        }
        return pts;
    }

    /// <summary>SDK CreoColor → native pro_color(与读回方向的 method 分流互逆)。</summary>
    private static GNS.pro_color ToNativeColor(CreoColor color)
    {
        var native = default(GNS.pro_color);
        if (color.IsDefault)
        {
            native.method = GNS.pro_color_method.PRO_COLOR_METHOD_DEFAULT;
        }
        else if (color.IsType)
        {
            native.method = GNS.pro_color_method.PRO_COLOR_METHOD_TYPE;
            native.value.type = (GNS.ProColortype)(int)color.NamedType!.Value;
        }
        else
        {
            native.method = GNS.pro_color_method.PRO_COLOR_METHOD_RGB;
            native.value.map.red = color.Red;
            native.value.map.green = color.Green;
            native.value.map.blue = color.Blue;
        }
        return native;
    }

    // GeneratedHelpers.ProDrawingDtlnoteVisit IntPtr overload 直绑
    // (symbol=IntPtr.Zero 枚举 drawing 自身注释)。
    // ProDtlitem ≡ pro_model_item，PtrToStructure 读 type/id。sheet=0 表示所有页。
    public IReadOnlyList<ItemRef> DrawingDtlnoteList(ModelIdentity model, int sheet)
    {
        if (!TryResolveMdl(model, out var mdl)) return Array.Empty<ItemRef>();
        var items = new List<ItemRef>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var mi = Marshal.PtrToStructure<GNS.pro_model_item>(item);
                items.Add(new ItemRef(model, (CreoModelItemType)(int)mi.type, mi.id));
            }
            catch (Exception ex) { caught = ex; return GNS.ProErrors.PRO_TK_GENERAL_ERROR; }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            // 使用 GeneratedHelpers IntPtr overload: symbol=IntPtr.Zero 枚举 drawing 自身注释
            var err = GeneratedHelpers.ProDrawingDtlnoteVisit(
                mdl, IntPtr.Zero, sheet, ptr, IntPtr.Zero, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                Check.Eval(nameof(G.ProDrawingDtlnoteVisit), err);
            }
        }
        finally { _visitRegistry.Unregister(cb); }

        if (caught != null) throw caught;
        return items;
    }
}
