using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtDrawing;

public static class PtDrawingRegistration
{
    public static void Register(CreoAppBuilder app, string modelName)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        // ProDrawingViewVisit(4 参 callback)遍历每个视图,收 DATA 快照(id/sheet/scale/name/solid)。
        app.Command("pt.drawing.list-views", "PT_DRAWING_LIST_VIEWS", "PT_DRAWING_LIST_VIEWS_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list views");
                return;
            }

            var result = dwg.ListViewsWithSkip();
            ctx.Messages.Info(
                $"list-views on {model.FullName}: count={result.Views.Count} skipped={result.SkippedCount}" +
                (result.Views.Count > 0
                    ? "; views=" + string.Join(",", result.Views.Take(10).Select(v => $"{v.Name}#{v.ViewId}@s{v.Sheet}"))
                    : ""));
        });

        // TestDrawing.c (pt_examples/pt_main, 去 ProMenu UI) — 纯查询切片 (零新 native / 零 SDK 改动)。
        RegisterPtDrawingSheetCount(app, modelName);
        RegisterPtDrawingViewCount(app, modelName);
        RegisterPtDrawingCurrentSheet(app, modelName);
        RegisterPtDrawingListTables(app, modelName);
        RegisterPtDrawingListDetailNotes(app, modelName);
        // 交互式选择探针 + detail group 反查(注册顺序即 token 顺序,勿动)
        RegisterPtDrawingSelectProbe(app);
        RegisterPtDrawingListDetailGroups(app, modelName);
        // 工程图表格写入探针
        RegisterPtDrawingTableRoundtrip(app, modelName);
        // pt_drawing draft entity 4 命令
        RegisterPtDrawingListDtlentities(app, modelName);
        RegisterPtDrawingDtlentityData(app, modelName);
        RegisterPtDrawingDtlentityCurveData(app, modelName);
        RegisterPtDrawingFixtureCreate(app, modelName);
    }

    /// <summary>init 回调期间 ProMdlCurrentGet 可能返 null(async 模式限制);
    /// 按已加载模型名回退 Retrieve,使命令不依赖 GetCurrent() 时序。</summary>
    private static CreoModel? ResolveDrawingOrCurrent(CreoSession session, string modelName)
    {
        var model = session.Models.GetCurrent();
        if (model is not null) return model;
        try { return session.Models.Retrieve(modelName, CreoModelType.Drawing); }
        catch { return null; }
    }

    /// <summary>表格写入探针: 2x2 表 → TextEnter 两格 → Display → 读回行列数+文本断言 → Delete。</summary>
    private static void RegisterPtDrawingTableRoundtrip(CreoAppBuilder app, string modelName)
    {
        app.Command("pt.drawing.table-roundtrip", "PT_DRAWING_TABLE_ROUNDTRIP", "PT_DRAWING_TABLE_ROUNDTRIP_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot create table");
                return;
            }

            // 构造 2x2 表格(字符单位;origin=(100,100))
            var spec = new DrawingTableSpec(
                origin: (100, 100),
                columnWidths: new double[] { 10, 10 },
                rowHeights: new double[] { 1, 1 },
                cells: new[]
                {
                    new DrawingTableCellText(1, 1, "CTK_T1"),
                    new DrawingTableCellText(2, 2, "CELL22"),
                });

            ctx.Messages.Info($"table-roundtrip: creating 2x2 table on {model.FullName}");
            var tableItem = dwg.CreateTable(spec);
            var tableId = tableItem.Id;
            ctx.Messages.Info($"table-roundtrip: table_created id={tableId}");

            // 读回行列数
            var rowCount = dwg.GetTableRowsCount(tableId);
            var colCount = dwg.GetTableColumnsCount(tableId);
            ctx.Messages.Info($"table-roundtrip: rows={rowCount} columns={colCount}");

            // 读回单元格文本
            var cell11 = dwg.ReadTableCellText(tableId, 1, 1);
            var cell22 = dwg.ReadTableCellText(tableId, 2, 2);
            var text11 = cell11.Length > 0 ? cell11[0] : "(empty)";
            var text22 = cell22.Length > 0 ? cell22[0] : "(empty)";
            ctx.Messages.Info($"table-roundtrip: cell(1,1)='{text11}' cell(2,2)='{text22}'");

            // 断言验证
            bool ok = rowCount == 2 && colCount == 2
                   && text11 == "CTK_T1" && text22 == "CELL22";
            ctx.Messages.Info($"table-roundtrip: assertions={ok}");

            // 清理: 删除表格
            dwg.DeleteTable(tableId);
            ctx.Messages.Info($"table-roundtrip: table_deleted id={tableId}");

            ctx.Messages.Info(ok
                ? $"table-roundtrip complete on {model.FullName}"
                : $"table-roundtrip FAILED on {model.FullName}");
        });
    }

    private static void RegisterPtDrawingListDtlentities(CreoAppBuilder app, string modelName)
    {
        // ProDrawingDtlentitiesCollect → CreoDrawing.ListDtlentities 切片
        app.Command("pt.drawing.list-dtlentities", "PT_DRAWING_LIST_DTLENTITIES", "PT_DRAWING_LIST_DTLENTITIES_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list dtlentities");
                return;
            }
            var entities = dwg.ListDtlentities(sheet: 0);
            ctx.Messages.Info($"list-dtlentities on {model.FullName}: count={entities.Count} (all sheets)");
        });
    }

    private static void RegisterPtDrawingDtlentityData(CreoAppBuilder app, string modelName)
    {
        // ProDtlentityDataGet → CreoDrawing.GetDtlentityData 切片
        // 取 entity 列表第一项 + 调 GetDtlentityData + 输出 color/font/width 或 null reason
        app.Command("pt.drawing.dtlentity-data", "PT_DRAWING_DTLENTITY_DATA", "PT_DRAWING_DTLENTITY_DATA_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot read dtlentity data");
                return;
            }
            var entities = dwg.ListDtlentities(sheet: 0);
            if (entities.Count == 0)
            {
                ctx.Messages.Info($"dtlentity-data on {model.FullName}: no draft entities");
                return;
            }
            var first = entities[0];
            var data = dwg.GetDtlentityData(first);
            if (data is null)
            {
                ctx.Messages.Info($"dtlentity-data on {model.FullName}: entity id={first.Id} -> null (data unresolved)");
                return;
            }
            var color = data.Value.Color.IsDefault ? "default"
                      : data.Value.Color.IsType    ? $"type={data.Value.Color.NamedType}"
                                                   : $"rgb=({data.Value.Color.Red:F2},{data.Value.Color.Green:F2},{data.Value.Color.Blue:F2})";
            ctx.Messages.Info($"dtlentity-data on {model.FullName}: id={first.Id} color={color} font={data.Value.FontName} width={data.Value.LineWidth:F3}");
        });
    }

    private static void RegisterPtDrawingSheetCount(CreoAppBuilder app, string modelName)
    {
        // TestDrawing.c ProDrawingSheetCount 切片。
        app.Command("pt.drawing.sheet-count", "PT_DRAWING_SHEET_COUNT", "PT_DRAWING_SHEET_COUNT_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot query sheet count");
                return;
            }
            var count = dwg.GetSheetsCount();
            ctx.Messages.Info(count is null
                ? $"sheet-count on {model.FullName}: (unsupported)"
                : $"sheet-count on {model.FullName}: {count}");
        });
    }

    private static void RegisterPtDrawingViewCount(CreoAppBuilder app, string modelName)
    {
        // TestDrawing.c ProDrawingViewsCount 切片。
        app.Command("pt.drawing.view-count", "PT_DRAWING_VIEW_COUNT", "PT_DRAWING_VIEW_COUNT_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot query view count");
                return;
            }
            var count = dwg.GetViewsCount();
            ctx.Messages.Info(count is null
                ? $"view-count on {model.FullName}: (unsupported)"
                : $"view-count on {model.FullName}: {count}");
        });
    }

    private static void RegisterPtDrawingCurrentSheet(CreoAppBuilder app, string modelName)
    {
        // TestDrawing.c ProDrawingCurrentSheetGet 切片。
        app.Command("pt.drawing.current-sheet", "PT_DRAWING_CURRENT_SHEET", "PT_DRAWING_CURRENT_SHEET_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot query current sheet");
                return;
            }
            var idx = dwg.GetCurrentSheetIndex();
            ctx.Messages.Info(idx is null
                ? $"current-sheet on {model.FullName}: (unsupported)"
                : $"current-sheet on {model.FullName}: idx={idx}");
        });
    }

    private static void RegisterPtDrawingListTables(CreoAppBuilder app, string modelName)
    {
        // TestDrawing.c ProDwgtable 系列 → CreoDrawing.ListTables (经 ProDrawingTableVisit 直绑) 切片。
        app.Command("pt.drawing.list-tables", "PT_DRAWING_LIST_TABLES", "PT_DRAWING_LIST_TABLES_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list tables");
                return;
            }
            var tables = dwg.ListTables();
            ctx.Messages.Info($"list-tables on {model.FullName}: count={tables.Count}");
        });
    }

    private static void RegisterPtDrawingListDetailNotes(CreoAppBuilder app, string modelName)
    {
        // TestDrawing.c ProDtlnote 系列 → CreoDrawing.ListDetailNotes (经 ProDrawingDtlnoteVisit 直绑) 切片。
        app.Command("pt.drawing.list-detail-notes", "PT_DRAWING_LIST_DETAIL_NOTES", "PT_DRAWING_LIST_DETAIL_NOTES_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list detail notes");
                return;
            }
            var notes = dwg.ListDetailNotes(sheet: 0);
            ctx.Messages.Info($"list-detail-notes on {model.FullName}: count={notes.Count} (all sheets)");
        });
    }

    /// <summary>ProSelect 交互式探针: 用户在 Creo UI 里点选, 回读每条 selection 的 (type,id) 反查真类型。</summary>
    private static void RegisterPtDrawingSelectProbe(CreoAppBuilder app)
    {
        // drawing context: option "any" 真机无法选中 → tkuse 官方 filter token 多值,
        // 覆盖 drawing 主要可选类型,让 UI 弹的选择器包全。
        // 另含 edge/curve/surface:标注类 token(draft_ent 等)属官方
        // tkuse 表 Drawing Items 分组,不含 view 里 part 几何投影(Geometry Items 分组)——
        // fixture(flange.drw)无 dim/note/gtol 时,用户点视图里的实际部件边线选不中。
        var drawingFilters = CreoSelectFilters.Join(
            CreoSelectFilters.DraftEntity, CreoSelectFilters.DetailSymbol,
            CreoSelectFilters.Note, CreoSelectFilters.DrawingTable,
            CreoSelectFilters.Dimension, CreoSelectFilters.RefDimension,
            CreoSelectFilters.Gtol,
            CreoSelectFilters.Edge, CreoSelectFilters.Curve, CreoSelectFilters.Surface);

        app.Command("pt.drawing.select-probe", "PT_DRAWING_SELECT_PROBE", "PT_DRAWING_SELECT_PROBE_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            CreoSelectionSet picks;
            try { picks = session.Selections.Select(drawingFilters, maxNumSels: 10); }
            catch (Exception ex) { ctx.Messages.Info($"select-probe: ProSelect 失败 {ex.GetType().Name}: {ex.Message}"); return; }
            using (picks) // owned 副本集,探针读完即释放
            {
                if (picks.Count == 0)
                {
                    ctx.Messages.Info("select-probe: count=0");
                    ctx.Messages.Info("select-probe: 未选中任何 item (middle-click 取消?)");
                    return;
                }
                var lines = string.Join(" | ", picks.Select(p =>
                    $"{p.TypeName}(code={p.RawTypeCode})#{p.Id}@{p.SelModel?.Name ?? "?"}"));
                ctx.Messages.Info($"select-probe: count={picks.Count}; {lines}");
            }
        });
    }

    /// <summary>ProDrawingDtlgroupsCollect → CreoDrawing.ListDetailGroups 切片(Sketch tab view-related 反查)。</summary>
    private static void RegisterPtDrawingListDetailGroups(CreoAppBuilder app, string modelName)
    {
        app.Command("pt.drawing.list-dtlgroups", "PT_DRAWING_LIST_DTLGROUPS", "PT_DRAWING_LIST_DTLGROUPS_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot list dtlgroups");
                return;
            }
            var groups = dwg.ListDetailGroups(sheet: 0);
            var detail = groups.Count > 0
                ? "; ids=" + string.Join(",", groups.Take(10).Select(g => g.Id.ToString()))
                : "";
            ctx.Messages.Info($"list-dtlgroups on {model.FullName}: count={groups.Count}{detail}");
        });
    }

    /// <summary>curve 几何读取命令:遍历全部 draft entity, 输出每个的 curve kind + 几何摘要。</summary>
    private static void RegisterPtDrawingDtlentityCurveData(CreoAppBuilder app, string modelName)
    {
        app.Command("pt.drawing.dtlentity-curve-data", "PT_DRAWING_DTLENTITY_CURVE_DATA", "PT_DRAWING_DTLENTITY_CURVE_DATA_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot read curve data");
                return;
            }
            // Collect + id 探测双路合并:view-related 实体 Collect 一律收不到,
            // probe 按 (owner,id) 直取补收;混合 drawing(顶层+view-related 并存)
            // 两路都有产出,按 id 去重 union
            var entities = dwg.ListDtlentities(sheet: 0);
            var items = entities.Select(e => (e.Id, Data: dwg.GetDtlentityData(e))).ToList();
            var collectIds = new HashSet<int>(items.Select(i => i.Id));
            var probed = dwg.ProbeDtlentities()
                .Where(p => !collectIds.Contains(p.Entity.Id))
                .Select(p => (p.Entity.Id, Data: (CreoDtlentityData?)p.Data)).ToList();
            var source = (items.Count, probed.Count) switch
            {
                (> 0, > 0) => "collect+probe",
                (> 0, _) => "collect",
                (_, > 0) => "probe",
                _ => "none",
            };
            items.AddRange(probed);
            if (items.Count == 0)
            {
                ctx.Messages.Info($"dtlentity-curve-data on {model.FullName}: no draft entities (collect+probe)");
                return;
            }
            int curveCount = 0;
            int noCurveCount = 0;
            foreach (var (entId, data) in items)
            {
                if (data is null)
                {
                    ctx.Messages.Info($"  id={entId} -> data=null");
                    continue;
                }
                var c = data.Value.Curve;
                if (c is null)
                {
                    noCurveCount++;
                    ctx.Messages.Info($"  id={entId} color={FormatColor(data.Value.Color)} curve=null");
                    continue;
                }
                curveCount++;
                var cv = c.Value;
                var geo = cv.Kind switch
                {
                    CreoDtlentityCurveKind.Line => $"end1=({cv.End1?.X:F2},{cv.End1?.Y:F2},{cv.End1?.Z:F2}) end2=({cv.End2?.X:F2},{cv.End2?.Y:F2},{cv.End2?.Z:F2})",
                    CreoDtlentityCurveKind.Arc => $"origin=({cv.Origin?.X:F2},{cv.Origin?.Y:F2},{cv.Origin?.Z:F2}) r={cv.Radius:F2} a=[{cv.StartAngle:F4},{cv.EndAngle:F4}]",
                    CreoDtlentityCurveKind.Circle => $"center=({cv.Center?.X:F2},{cv.Center?.Y:F2},{cv.Center?.Z:F2}) r={cv.Radius:F2}",
                    CreoDtlentityCurveKind.BSpline => $"knots={cv.NumPoints} cpts={cv.NumControlPoints}",
                    CreoDtlentityCurveKind.Spline => $"npts={cv.NumPoints}",
                    CreoDtlentityCurveKind.Text => $"loc=({cv.Origin?.X:F2},{cv.Origin?.Y:F2},{cv.Origin?.Z:F2})",
                    CreoDtlentityCurveKind.Point => $"pos=({cv.Origin?.X:F2},{cv.Origin?.Y:F2},{cv.Origin?.Z:F2})",
                    CreoDtlentityCurveKind.Ellipse => $"center=({cv.Origin?.X:F2},{cv.Origin?.Y:F2},{cv.Origin?.Z:F2}) r={cv.Radius:F2}",
                    _ => $"(unhandled kind={cv.Kind})",
                };
                ctx.Messages.Info($"  id={entId} kind={cv.Kind} {geo} color={FormatColor(data.Value.Color)}");
            }
            ctx.Messages.Info($"dtlentity-curve-data on {model.FullName}: total={items.Count} curves={curveCount} noCurve={noCurveCount} src={source}");
        });
    }

    private static string FormatColor(CreoColor color)
        => color.IsDefault ? "default"
         : color.IsType    ? $"type={color.NamedType}"
                           : $"rgb=({color.Red:F2},{color.Green:F2},{color.Blue:F2})";

    // ---- fixture 程序化生成 (golden values 真源) ----

    /// <summary>drawing fixture 的 golden 常量表(代码即真源)。
    /// 覆盖 7 种 curve kind + Default/Type/Rgb 三色变体。
    /// Spline/BSpline 仅 golden 计数(点数据由 bridge 确定性生成)。</summary>
    public static readonly (string Label, CreoDtlentityCurve Curve, CreoColor Color)[] FixtureGoldenEntities =
    {
        ("line-rgb-red", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Line,
            End1: (100, 100, 0), End2: (200, 150, 0),
            Origin: null, Vector1: null, Vector2: null,
            StartAngle: null, EndAngle: null, Radius: null,
            Center: null, Normal: null, NumPoints: 2, NumControlPoints: 0),
            CreoColor.FromRgb(1, 0, 0)),

        ("arc-type-letter", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Arc,
            End1: null, End2: null,
            Origin: (300, 100, 0), Vector1: (1, 0, 0), Vector2: (0, 1, 0),
            StartAngle: 0, EndAngle: 1.5707963267948966, Radius: 50,
            Center: null, Normal: null, NumPoints: 0, NumControlPoints: 0),
            CreoColor.FromType(CreoColorType.Letter)),

        ("circle-default", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Circle,
            End1: null, End2: null,
            Origin: null, Vector1: null, Vector2: null,
            StartAngle: null, EndAngle: null, Radius: 40,
            Center: (450, 100, 0), Normal: (0, 0, 1), NumPoints: 0, NumControlPoints: 0),
            CreoColor.Default),

        ("point-default", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Point,
            End1: null, End2: null,
            Origin: (550, 100, 0), Vector1: null, Vector2: null,
            StartAngle: null, EndAngle: null, Radius: null,
            Center: null, Normal: null, NumPoints: 1, NumControlPoints: 0),
            CreoColor.Default),

        ("ellipse-rgb-green", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Ellipse,
            End1: null, End2: null,
            Origin: (650, 100, 0), Vector1: (1, 0, 0), Vector2: (0, 0, 1),
            StartAngle: 0, EndAngle: 6.283185307179586, Radius: 60,
            Center: null, Normal: null, NumPoints: 0, NumControlPoints: 0,
            MinorRadius: 30),
            CreoColor.FromRgb(0, 1, 0)),

        ("spline-default", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.Spline,
            End1: null, End2: null,
            Origin: null, Vector1: null, Vector2: null,
            StartAngle: null, EndAngle: null, Radius: null,
            Center: null, Normal: null, NumPoints: 5, NumControlPoints: 0),
            CreoColor.Default),

        // BSpline: degree=3 时 knot 数 = 控制点数 + degree + 1 = 4 + 3 + 1 = 8
        ("bspline-type-letter", new CreoDtlentityCurve(
            CreoDtlentityCurveKind.BSpline,
            End1: null, End2: null,
            Origin: null, Vector1: null, Vector2: null,
            StartAngle: null, EndAngle: null, Radius: null,
            Center: null, Normal: null, NumPoints: 8, NumControlPoints: 4),
            CreoColor.FromType(CreoColorType.Letter)),
    };

    /// <summary>fixture 生成命令: 在当前 drawing 上按 golden 表程序化创建 7 条 draft entity
    /// 并保存,同时输出 golden 清单。
    /// <para>未覆盖的 kind: Text(ptc_text 需 wchar 数组 + 字体属性,无 Init 函数,直填风险高) /
    /// Arrow / Polygon / CompositeCurve / SurfaceCurve /
    /// ParamCurve(drawing draft 场景无程序化创建入口或语义不适用)。</para></summary>
    private static void RegisterPtDrawingFixtureCreate(CreoAppBuilder app, string modelName)
    {
        app.Command("pt.drawing.fixture-create", "PT_DRAWING_FIXTURE_CREATE", "PT_DRAWING_FIXTURE_CREATE_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;
            var model = ResolveDrawingOrCurrent(session, modelName);
            if (model is not CreoDrawing dwg)
            {
                ctx.Messages.Info($"model {model?.Name ?? modelName} is not a drawing, cannot create fixture entities");
                return;
            }

            int created = 0, failed = 0;
            foreach (var (label, curve, color) in FixtureGoldenEntities)
            {
                var id = dwg.CreateDraftEntity(curve, color);
                if (id is null)
                {
                    failed++;
                    ctx.Messages.Info($"  {label}: FAILED");
                    continue;
                }
                created++;
                ctx.Messages.Info($"  {label}: id={id} kind={curve.Kind} color={FormatColor(color)}");
            }

            // 保存结果必须是真实结果:Save() 抛异常即 false,不许用 created>0 冒充。
            // 注意 Creo Save 落盘语义:写"版本化新文件"(flange.drw.N)且写回模型 retrieve
            // 的原目录——不覆盖原文件,原 .drw 时间戳不变是正常现象。
            var saved = false;
            if (created > 0)
            {
                try { dwg.Save(); saved = true; }
                catch (Exception ex)
                {
                    ctx.Messages.Info($"  save FAILED: {ex.GetType().Name}: {ex.Message}");
                }
            }

            // 双口径回读自校验:Collect 收 sheet 顶层项(view-related 一律收不到);
            // probe 按 id 直取 DataGet,是 view-related 唯一读回口径。
            var readback = dwg.ListDtlentities(sheet: 0).Count;
            var probeback = dwg.ProbeDtlentities().Count;

            // 结构化结果事件:created/failed/saved/readback/probeback 进 props,
            // 结构化字段供程序断言——message bar 文本无法程序化消费
            CreoAppLog.Info(
                $"fixture-create on {model.FullName}: created={created} failed={failed} saved={saved} readback={readback} probeback={probeback}",
                @event: "drawing.fixture-create.result",
                module: "PtDrawing",
                props: new { model = model.FullName, created, failed, saved, readback, probeback });

            ctx.Messages.Info($"fixture-create on {model.FullName}: created={created} failed={failed} saved={saved} readback={readback} probeback={probeback}");
        });
    }
}
