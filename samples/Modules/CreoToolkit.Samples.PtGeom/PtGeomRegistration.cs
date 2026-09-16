using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtGeom;

/// <summary>
/// 示例移植 — TestGeom.c ProTestGeomTraverse / ProTestPartTraverse 核心遍历切片。
///
/// C 端原始逻辑（去掉 ProMenu/ProSelect UI 层后）：
///   TestGeom.c ProTestPartTraverse (L1320-1380):
///     ProUtilCollectSolidSurfaces → ProTestSurfaceAct → ProSurfaceIdGet + ProSurfaceTypeGet
///     ProUtilCollectSolidQuilts   → ProTestQuiltAct   → ProQuiltIdGet
///     ProUtilCollectSolidAxis     → ProTestAxisAct    → ProModelitemNameGet
///     ProUtilCollectSolidCsys     → ProTestCsysAct    → ProModelitemNameGet
///
/// .NET 端实现：完全通过 L3 API (CreoSolid.ListAxes/Csys/Surfaces/Quilts) 实现，零 native 调用。
/// </summary>
public static class PtGeomRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtGeomListAxes(app, modelName, modelType);
        RegisterPtGeomListCsys(app, modelName, modelType);
        RegisterPtGeomCsysFirstMatrix(app, modelName, modelType);
        RegisterPtGeomSurfPlaneInfo(app, modelName, modelType);
        RegisterPtGeomCsysCoordSystem(app, modelName, modelType);
        RegisterPtGeomListSurfaces(app, modelName, modelType);
        RegisterPtGeomListQuilts(app, modelName, modelType);
        RegisterPtGeomListRelsets(app, modelName, modelType);
        RegisterPtGeomEvalAxesAngle(app, modelName, modelType);
        RegisterPtGeomEvalAxesDistance(app, modelName, modelType);
        RegisterPtGeomEvalSurfaceDiameter(app, modelName, modelType);
        RegisterPtGeomCableBoundaries(app, modelName, modelType);

        // 给 PtGeom 9 命令挂 menubar 入口,
        // 让"手动启 Creo + 点菜单"路径可达(对齐 PtBasic / AgentDemo 既有模式)。
        // MenuAdd/MenuButton 不分配 token。
    }

    /// <summary>
    /// 取首个 PLANE 类型 surface → CreoSurface.GetPlane → CreoPlane 演示。
    /// 打印 Origin / Normal(原长保留)/(0,0,0) 到该平面的 SignedDistance + Normal-flipped(0,0,0) 的 ClosestPointTo 投影。
    /// 示例端到端覆盖:Bridge ProSurfacedataGet+ProPlanedataGet → SurfacePlaneData → CreoPlane → SignedDistanceTo/ClosestPointTo。
    /// </summary>
    private static void RegisterPtGeomSurfPlaneInfo(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.surf-plane-info", "PT_GEOM_SURF_PLANE_INFO", "PT_GEOM_SURF_PLANE_INFO_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot get surface plane info");
                return;
            }
            var surfaces = solid.ListSurfaces();
            CreoSurface? firstPlane = null;
            CreoPlane? plane = null;
            foreach (var s in surfaces)
            {
                if (s is not CreoSurface cs) continue;
                var p = cs.GetPlane();
                if (p is not null) { firstPlane = cs; plane = p; break; }
            }
            if (firstPlane is null || plane is null)
            {
                ctx.Messages.Info($"surf-plane-info on {model.FullName}: no PLANE-type surface (scanned {surfaces.Count})");
                return;
            }
            var pl = plane.Value;
            double signedToOrigin = pl.SignedDistanceTo(CreoVec3.Zero);
            var footOfOrigin = pl.ClosestPointTo(CreoVec3.Zero);
            ctx.Messages.Info(
                $"surf-plane-info on {model.FullName}: " +
                $"surface id={firstPlane.Id} " +
                $"origin=({pl.Origin.X:F3},{pl.Origin.Y:F3},{pl.Origin.Z:F3}) " +
                $"normal=({pl.Normal.X:F3},{pl.Normal.Y:F3},{pl.Normal.Z:F3})|{pl.Normal.Length():F3}| " +
                $"signed-dist(model-origin)={signedToOrigin:F3} " +
                $"foot-of-origin=({footOfOrigin.X:F3},{footOfOrigin.Y:F3},{footOfOrigin.Z:F3})");
        });
    }

    /// <summary>
    /// 取首个 csys → CreoCsys.GetCoordSystem → CreoCoordSystem 演示。
    /// 打印 Origin + X/Y/Z 三轴 + 单位长检查(IsOrthonormal 经 ToMatrix 转回 CreoMat4 判正交)。
    /// </summary>
    private static void RegisterPtGeomCsysCoordSystem(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.csys-coord-system", "PT_GEOM_CSYS_COORD_SYSTEM", "PT_GEOM_CSYS_COORD_SYSTEM_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot get csys coord-system");
                return;
            }
            var first = solid.ListCsys().OfType<CreoCsys>().FirstOrDefault();
            if (first is null)
            {
                ctx.Messages.Info($"csys-coord-system on {model.FullName}: no csys");
                return;
            }
            var cs = first.GetCoordSystem();
            if (cs is null)
            {
                ctx.Messages.Info($"csys-coord-system on {model.FullName}: csys {first.GetName() ?? "?"} data unavailable");
                return;
            }
            var c = cs.Value;
            ctx.Messages.Info(
                $"csys-coord-system on {model.FullName}: " +
                $"csys={first.GetName() ?? "?"} " +
                $"origin=({c.Origin.X:F3},{c.Origin.Y:F3},{c.Origin.Z:F3}) " +
                $"X=({c.XAxis.X:F3},{c.XAxis.Y:F3},{c.XAxis.Z:F3}) " +
                $"Y=({c.YAxis.X:F3},{c.YAxis.Y:F3},{c.YAxis.Z:F3}) " +
                $"Z=({c.ZAxis.X:F3},{c.ZAxis.Y:F3},{c.ZAxis.Z:F3}) " +
                $"orthonormal={c.ToMatrix().IsOrthonormal()}");
        });
    }

    /// <summary>
    /// 对应 TestGeom.c ProTestPartTraverse (L1355-1360) + ProTestAxisAct callback：
    ///   ProUtilCollectSolidAxis → ProTestAxisAct → ProModelitemNameGet 打印每条轴名。
    /// L3 端: (CreoSolid).ListAxes() → 遍历 ICreoModelItem → GetName()。
    /// </summary>
    private static void RegisterPtGeomListAxes(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestGeom.c L1355: ProUtilCollectSolidAxis(solid, NULL, axes) → ProTestAxisAct
        app.Command("pt.geom.list-axes", "PT_GEOM_LIST_AXES", "PT_GEOM_LIST_AXES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list axes");
                return;
            }

            // 对应 ProUtilCollectSolidAxis + ProTestAxisAct: ProModelitemNameGet per axis
            var axes = solid.ListAxes();
            var names = axes.Select(a => a.GetName()).ToList();

            ctx.Messages.Info(
                $"list-axes on {model.FullName}: count={axes.Count}" +
                (names.Count > 0 ? "; " + string.Join(", ", names.Take(10)) : ""));
        });
    }

    /// <summary>
    /// 对应 TestGeom.c ProTestPartTraverse (L1362-1367) + ProTestCsysAct callback：
    ///   ProUtilCollectSolidCsys → ProTestCsysAct → ProModelitemNameGet 打印每个坐标系名。
    /// L3 端: (CreoSolid).ListCsys() → 遍历 ICreoModelItem → GetName()。
    /// </summary>
    private static void RegisterPtGeomListCsys(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestGeom.c L1362: ProUtilCollectSolidCsys(solid, NULL, csys) → ProTestCsysAct
        app.Command("pt.geom.list-csys", "PT_GEOM_LIST_CSYS", "PT_GEOM_LIST_CSYS_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list csys");
                return;
            }

            // 对应 ProUtilCollectSolidCsys + ProTestCsysAct: ProModelitemNameGet per csys
            var csysList = solid.ListCsys();
            var names = csysList.Select(c => c.GetName()).ToList();

            ctx.Messages.Info(
                $"list-csys on {model.FullName}: count={csysList.Count}" +
                (names.Count > 0 ? "; " + string.Join(", ", names.Take(10)) : ""));
        });
    }

    /// <summary>
    /// 演示 <see cref="CreoCsys.GetMatrix"/> + <see cref="CreoTransform.TransformPoint"/> 联用。
    /// 取首个 csys → GetMatrix → 把局部 (0,0,0) 变到模型坐标系(应得 csys origin)。
    /// 旧版 PtkMatrix 需经 ProGeomitemdata 抽出 csys data 再手动塞矩阵,本切片单行 GetMatrix 完成。
    /// </summary>
    private static void RegisterPtGeomCsysFirstMatrix(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.csys-first-matrix", "PT_GEOM_CSYS_FIRST_MATRIX", "PT_GEOM_CSYS_FIRST_MATRIX_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot get csys matrix");
                return;
            }
            var csysList = solid.ListCsys();
            var first = csysList.OfType<CreoCsys>().FirstOrDefault();
            if (first is null)
            {
                ctx.Messages.Info($"csys-first-matrix on {model.FullName}: no csys (count={csysList.Count})");
                return;
            }
            var matrix = first.GetMatrix();
            if (matrix is null)
            {
                ctx.Messages.Info($"csys-first-matrix on {model.FullName}: csys {first.GetName() ?? "?"} matrix unavailable");
                return;
            }
            // 局部 Zero 经 csys 矩阵 → 模型坐标系下的 csys origin (= 矩阵第 4 行 M30/M31/M32)
            var m = matrix.Value;
            var originFromTransform = m.TransformPoint(CreoVec3.Zero);
            ctx.Messages.Info(
                $"csys-first-matrix on {model.FullName}: " +
                $"csys={first.GetName() ?? "?"} " +
                $"origin=({m.M30:F3},{m.M31:F3},{m.M32:F3}) " +
                $"transform(0,0,0)=({originFromTransform.X:F3},{originFromTransform.Y:F3},{originFromTransform.Z:F3})");
        });
    }

    /// <summary>
    /// 对应 TestGeom.c ProTestPartTraverse (L1327-1342) + ProTestSurfaceAct callback：
    ///   ProUtilCollectSolidSurfaces → ProTestSurfaceAct → ProSurfaceIdGet + ProSurfaceTypeGet。
    /// L3 端: (CreoSolid).ListSurfaces() → 遍历 ICreoModelItem → GetId()。
    /// </summary>
    private static void RegisterPtGeomListSurfaces(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestGeom.c L1327: ProUtilCollectSolidSurfaces(solid, NULL, surfaces) → ProTestSurfaceAct
        app.Command("pt.geom.list-surfaces", "PT_GEOM_LIST_SURFACES", "PT_GEOM_LIST_SURFACES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list surfaces");
                return;
            }

            // 对应 ProUtilCollectSolidSurfaces + ProTestSurfaceAct: ProSurfaceIdGet per surface
            var surfaces = solid.ListSurfaces();
            // Id 在 ICreoModelItem 接口上（零 I/O 属性），无需转型
            var ids = surfaces.Select(s => s.Id.ToString()).ToList();

            ctx.Messages.Info(
                $"list-surfaces on {model.FullName}: count={surfaces.Count}" +
                (ids.Count > 0 ? "; ids=" + string.Join(",", ids.Take(10)) : ""));
        });
    }

    /// <summary>
    /// 对应 TestGeom.c ProTestPartTraverse (L1345-1353) + ProTestQuiltAct callback：
    ///   ProUtilCollectSolidQuilts → ProTestQuiltAct → ProQuiltIdGet 打印每个 quilt ID。
    /// L3 端: (CreoSolid).ListQuilts() → 遍历 ICreoModelItem → GetId()。
    /// </summary>
    private static void RegisterPtGeomListQuilts(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestGeom.c L1345: ProUtilCollectSolidQuilts(solid, NULL, quilts) → ProTestQuiltAct
        app.Command("pt.geom.list-quilts", "PT_GEOM_LIST_QUILTS", "PT_GEOM_LIST_QUILTS_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list quilts");
                return;
            }

            // 对应 ProUtilCollectSolidQuilts + ProTestQuiltAct: ProQuiltIdGet per quilt
            var quilts = solid.ListQuilts();
            // ICreoModelItem → CreoQuilt 转型取 id（OHandle，GetId 在具体类，不在接口）
            var ids = quilts.OfType<CreoQuilt>().Select(q => q.Id.ToString()).ToList();

            ctx.Messages.Info(
                $"list-quilts on {model.FullName}: count={quilts.Count}" +
                (ids.Count > 0 ? "; ids=" + string.Join(",", ids.Take(10)) : ""));
        });
    }

    /// <summary>
    /// `ProSolidRelsetVisit` 2 参签名(与 3 参 SolidVisitor 不同 ABI)。
    /// L3 端: (CreoSolid).ListRelationSets() → 遍历 ICreoModelItem → GetName()。
    /// </summary>
    private static void RegisterPtGeomListRelsets(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.list-relsets", "PT_GEOM_LIST_RELSETS", "PT_GEOM_LIST_RELSETS_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot list relsets");
                return;
            }

            // 对应 ProSolidRelsetVisit (2 参 callback,与 3 参 SolidVisitor 不同 ABI)。
            var relsets = solid.ListRelationSets();
            var names = relsets.Select(r => r.GetName() ?? "(no-name)").ToList();

            ctx.Messages.Info(
                $"list-relsets on {model.FullName}: count={relsets.Count}" +
                (names.Count > 0 ? "; " + string.Join(", ", names.Take(10)) : ""));
        });
    }

    /// <summary>
    /// 对应 TestMeasure.c USER_ANGLE_EVAL 切片(L107/L133):
    ///   ProSelect("edge", 2) + ProGeomitemAngleEval(sel[0], sel[1], &dist) 打印角度。
    /// .NET 端:取前 2 条轴(ListAxes)代替"用户选 2 个 edge"——示例无 UI;
    /// L3 端 (CreoSolid).EvaluateAngle(axis1, axis2) 内部 ProSelectionAlloc/Eval/Free 临时包装。
    /// 轴数 &lt; 2 时降级为提示,不算错。
    /// </summary>
    private static void RegisterPtGeomEvalAxesAngle(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.eval-axes-angle", "PT_GEOM_EVAL_AXES_ANGLE", "PT_GEOM_EVAL_AXES_ANGLE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot eval axes-angle");
                return;
            }
            var axes = solid.ListAxes();
            if (axes.Count < 2)
            {
                ctx.Messages.Info($"eval-axes-angle on {model.FullName}: axes count={axes.Count} < 2, skip");
                return;
            }
            var angle = solid.EvaluateAngle(axes[0], axes[1]);
            ctx.Messages.Info(
                $"eval-axes-angle on {model.FullName}: " +
                $"{axes[0].GetName() ?? "?"} ↔ {axes[1].GetName() ?? "?"} → " +
                (angle.HasValue ? angle.Value.ToString("F3") + " deg" : "(n/a)"));
        });
    }

    /// <summary>
    /// 对应 TestMeasure.c USER_DISTANCE_EVAL 切片(L110/L139):
    ///   ProSelect("surface,point,axis", 2) + ProGeomitemDistanceEval(sel[0], sel[1], &dist)。
    /// .NET 端:取前 2 条轴(常见用例)算距离;L3 端 (CreoSolid).EvaluateDistance。
    /// </summary>
    private static void RegisterPtGeomEvalAxesDistance(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.eval-axes-distance", "PT_GEOM_EVAL_AXES_DISTANCE", "PT_GEOM_EVAL_AXES_DISTANCE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot eval axes-distance");
                return;
            }
            var axes = solid.ListAxes();
            if (axes.Count < 2)
            {
                ctx.Messages.Info($"eval-axes-distance on {model.FullName}: axes count={axes.Count} < 2, skip");
                return;
            }
            var dist = solid.EvaluateDistance(axes[0], axes[1]);
            ctx.Messages.Info(
                $"eval-axes-distance on {model.FullName}: " +
                $"{axes[0].GetName() ?? "?"} ↔ {axes[1].GetName() ?? "?"} → " +
                (dist.HasValue ? dist.Value.ToString("F3") : "(n/a)"));
        });
    }

    /// <summary>
    /// 对应 TestMeasure.c USER_DIAMETER_EVAL 切片(L114/L145):
    ///   ProSelect("surface", 1) + ProGeomitemDiameterEval(sel[0], &dist)。
    /// .NET 端:遍历 ListSurfaces 取**第一个**能算出直径的面;L3 端 (CreoSolid).EvaluateDiameter。
    /// </summary>
    private static void RegisterPtGeomEvalSurfaceDiameter(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.eval-surface-diameter", "PT_GEOM_EVAL_SURFACE_DIAMETER", "PT_GEOM_EVAL_SURFACE_DIAMETER_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"model {modelName} is not a solid, cannot eval surface-diameter");
                return;
            }
            var surfaces = solid.ListSurfaces();
            ICreoModelItem? hit = null;
            double? value = null;
            // 非圆柱/圆锥 Pro 可能抛 BAD_INPUTS,逐个 try/catch 跳过,
            // 避免命令链路因首个平面就断;NotFound 由 SDK 已转 null,这里只兜真实异常。
            foreach (var s in surfaces)
            {
                try
                {
                    var d = solid.EvaluateDiameter(s);
                    if (d.HasValue) { hit = s; value = d; break; }
                }
                catch (CreoToolkit.Sdk.Errors.CreoException) { /* 非可测直径面,跳过 */ }
            }
            if (hit is null)
            {
                ctx.Messages.Info(
                    $"eval-surface-diameter on {model.FullName}: " +
                    $"no surface with diameter (scanned {surfaces.Count})");
                return;
            }
            ctx.Messages.Info(
                $"eval-surface-diameter on {model.FullName}: " +
                $"surface id={hit.Id} → {value!.Value:F3}");
        });
    }

    /// <summary>
    /// CreoSolid.GetCableSegmentBoundaries → ProCableLocationsOnSegEndGet:读取 harness/asm
    /// 内每个 cable 每段末端 boundary(start/end)。无 cable(通用零件)返"no cable"分支。
    /// cable id 从 CTK_APP_CABLE_ID env 或默认扫描 1..10 试。
    /// 注意:有效 cable 数据流未经真机验证(fixture 无 cable 模型),当前真机只证
    /// "无效 id 返空数组不 crash";catch 分支输出 errors 计数,不静默吞。
    /// </summary>
    private static void RegisterPtGeomCableBoundaries(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.geom.cable-boundaries", "PT_GEOM_CABLE_BOUNDARIES",
            "PT_GEOM_CABLE_BOUNDARIES_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoSolid solid)
                {
                    ctx.Messages.Info($"model {modelName} is not a solid, cannot dump cable boundaries");
                    return;
                }

                // cable id 可通过 env 指定;未指定则扫描 1..10 试
                var envCableId = Environment.GetEnvironmentVariable("CTK_APP_CABLE_ID");
                var cableIds = int.TryParse(envCableId, out var explicitId)
                    ? new[] { explicitId }
                    : Enumerable.Range(1, 10).ToArray();
                var iterations = int.TryParse(
                    Environment.GetEnvironmentVariable("CTK_CABLE_BOUNDARY_ITERATIONS"),
                    out var requestedIterations)
                    ? Math.Max(1, Math.Min(requestedIterations, 10000))
                    : 1;

                int hits = 0, errors = 0;
                var preview = new System.Collections.Generic.List<string>();
                foreach (var cid in cableIds)
                {
                    System.Collections.Generic.IReadOnlyList<CableSegmentBoundary>? bounds;
                    try
                    {
#pragma warning disable CTKEXP001 // Explicit diagnostic sample for the ownership-unknown API.
                        bounds = null;
                        for (var iteration = 0; iteration < iterations; iteration++)
                            bounds = solid.GetCableSegmentBoundaries(cid);
#pragma warning restore CTKEXP001
                    }
                    catch (CreoToolkit.Sdk.Errors.CreoException) { errors++; continue; }
                    if (bounds is null) continue;
                    hits++;
                    if (preview.Count < 5)
                        preview.Add($"cable#{cid}:segs={bounds.Count}");
                }

                if (hits == 0)
                {
                    ctx.Messages.Info(
                        $"cable-boundaries on {model.FullName}: no cable found in scan range " +
                        $"[{string.Join(",", cableIds)}]; errors={errors}; iterations={iterations}");
                    ctx.Messages.Info("cable-boundaries complete");
                    return;
                }

                ctx.Messages.Info(
                    $"cable-boundaries on {model.FullName}: cables={hits}; errors={errors}; iterations={iterations}; " +
                    string.Join(", ", preview));
                ctx.Messages.Info("cable-boundaries complete");
            });
    }

}
