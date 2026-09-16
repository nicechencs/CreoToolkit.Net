using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.HostProbe;

/// <summary>
/// host smoke 探针 sample:InitializeApp 路径下经 <c>CTK_APP_RUN_COMMAND</c> 触发的诊断命令,
/// 可作为手动调试的目标命令。
/// <para>未含 <c>SubscribeModelSavePre</c> + Save 触发的 smoke:程序化
/// <c>ProMdlSave</c> 不触发 <c>PRO_MODEL_SAVE_PRE</c>(仅 UI File→Save 触发);
/// events 域 smoke 改走真 UI 手动测试。</para>
/// </summary>
public static class HostProbeRegistration
{
    public const string DefaultSmokeModelName = "exercise4";

    public static void Register(CreoAppBuilder app)
        => Register(app, DefaultSmokeModelName, CreoModelType.Part);

    public static void Register(CreoAppBuilder app, string smokeModelName, CreoModelType smokeModelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(smokeModelName);

        app.Command("ctk.probe.smoke.model", "PROBE_SMOKE_MODEL", "PROBE_SMOKE_MODEL_HELP",
            ctx => RunModelSmoke(ctx, smokeModelName, smokeModelType));
    }

    private static void RunModelSmoke(CreoCommandContext ctx, string defaultModel, CreoModelType defaultType)
    {
        var session = ctx.Session;
        if (session is null)
        {
            ctx.Messages.Info("ctk.probe.smoke.model: 无当前会话,跳过。");
            return;
        }

        var modelName = Environment.GetEnvironmentVariable("CTK_HOST_SMOKE_MODEL");
        if (string.IsNullOrWhiteSpace(modelName)) modelName = defaultModel;
        var modelType = ReadSmokeModelType(defaultType);

        Log($"ctk.probe.smoke.model: retrieving model {modelName} ({modelType}).",
            @event: "probe.smoke.model.retrieve.begin",
            props: new { model = modelName, type = modelType.ToString() });
        var model = session.Models.Retrieve(modelName, modelType);
        Log($"ctk.probe.smoke.model: retrieve returned {model.Name} ({model.Type}).",
            @event: "probe.smoke.model.retrieve.ok",
            props: new { model = model.Name, type = model.Type.ToString() });

        // 提前到 selection/write 段前,避免下游未实现 stub 阻塞 smoke;
        // 两分支只依赖已 retrieve 的 model + session,不需 elemtree/selection。
        if (IsEnvEnabled("CTK_HOST_ALLOW_DIM_TEXT_SMOKE"))
        {
            RunDimTextSmoke(model);
        }
        if (IsEnvEnabled("CTK_HOST_ALLOW_RETRIEVE_BY_NAME_SMOKE"))
        {
            RunRetrieveByNameSmoke(session, modelName, modelType);
        }
        if (IsEnvEnabled("CTK_HOST_ALLOW_MASS_PROPERTY_CSYS_SMOKE"))
        {
            RunMassPropertyCsysSmoke(model);
        }
        if (IsEnvEnabled("CTK_HOST_ALLOW_GEOMETRY_SMOKE"))
        {
            RunGeometryBridgeSmoke(model);
        }

        var first = session.Features.First(model);
        if (first is null)
        {
            Log("ctk.probe.smoke.model: no enumerable feature found.",
                @event: "probe.smoke.feature.first.none");
        }
        else
        {
            using var tree = first.ExtractElementTree();
            var nodeCount = tree.Walk().Count();
            Log($"ctk.probe.smoke.model: feature {first.Id} element nodes={nodeCount}.",
                @event: "probe.smoke.feature.elemtree.ok",
                props: new { featureId = first.Id, nodeCount });
        }

        using (var selection = session.Selections.CreateModelSelection(model))
        {
            Log($"ctk.probe.smoke.model: selection copies={selection.Count}.",
                @event: "probe.smoke.selection.pick.ok",
                props: new { copies = selection.Count });
        }

        if (IsEnvEnabled("CTK_HOST_ALLOW_MODEL_WRITE"))
        {
            var paramName = Environment.GetEnvironmentVariable("CTK_HOST_SMOKE_PARAM");
            if (string.IsNullOrWhiteSpace(paramName)) paramName = "CTK_HOST_SMOKE";
            var value = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            model.Parameters.Set(paramName, CreoParamValue.OfString(value));
            var found = model.Parameters.Find(paramName);
            if (found?.Value != CreoParamValue.OfString(value))
                throw new InvalidOperationException($"Parameter {paramName} round-trip failed.");
            Log($"ctk.probe.smoke.model: parameter {paramName} round-trip passed.",
                @event: "probe.smoke.parameter.roundtrip.ok",
                props: new { name = paramName, value });
        }
        else
        {
            Log("ctk.probe.smoke.model: write smoke skipped; set CTK_HOST_ALLOW_MODEL_WRITE=1 to enable.",
                @event: "probe.smoke.parameter.skipped",
                props: new { reason = "env.CTK_HOST_ALLOW_MODEL_WRITE.unset" });
        }

        session.AssertNoTrackedResourcesForDiagnostics();
        Log("ctk.probe.smoke.model: owned resources balanced.",
            @event: "probe.smoke.resources.balanced");
        ctx.Messages.Info("Host probe smoke ok.");
    }

    /// <summary>α-1 dim-text smoke:循环 ListDimensions + GetDisplayText,验 free 契约多轮无 crash。
    /// 同时支持 CreoSolid(走 ProSolidDimensionVisit)与 CreoDrawing(走 ProDrawingDimensionVisit);
    /// 后者拓扑上 dims 必现,α-1 命题(ProDimensionTextWstringsGet + ProWstringproarrayFree 递归 free)
    /// 只有 dims>0 才被真正调用。</summary>
    private static void RunDimTextSmoke(CreoModel model)
    {
        var iterations = ReadIntEnv("CTK_HOST_DIM_TEXT_ITERATIONS", defaultValue: 5, min: 1, max: 1000);
        Log($"ctk.probe.smoke.model: dim-text smoke begin iterations={iterations} modelKind={model.GetType().Name}.",
            @event: "probe.smoke.dim-text.begin",
            props: new { iterations, modelKind = model.GetType().Name });

        int firstDimCount = -1;
        long totalTextReads = 0;
        for (int i = 0; i < iterations; i++)
        {
            IReadOnlyList<ICreoModelItem> dims = model switch
            {
                CreoSolid solid => solid.ListDimensions(includeRefDimensions: true),
                CreoDrawing drawing => drawing.ListDimensions(),
                _ => Array.Empty<ICreoModelItem>(),
            };
            if (firstDimCount < 0) firstDimCount = dims.Count;
            foreach (var d in dims)
            {
                string? text = d switch
                {
                    CreoDimension dim => dim.GetDisplayText(),
                    CreoRefDimension refDim => refDim.GetDisplayText(),
                    _ => null,
                };
                if (text is not null) totalTextReads++;
            }
        }

        Log($"ctk.probe.smoke.model: dim-text smoke ok dims={firstDimCount} totalReads={totalTextReads}.",
            @event: "probe.smoke.dim-text.ok",
            props: new { dims = firstDimCount, iterations, totalReads = totalTextReads });
    }

    /// <summary>α-6 retrieve-by-name smoke:session-hit / search_path / miss 三分支实证。</summary>
    private static void RunRetrieveByNameSmoke(CreoSession session, string sessionModelName, CreoModelType sessionModelType)
    {
        // 分支 1:session 已在;RetrieveByName 命中会话缓存,不触磁盘。
        var sessionHit = session.Models.RetrieveByName(sessionModelName, sessionModelType);
        Log($"ctk.probe.smoke.model: retrieve-by-name session-hit result={(sessionHit is null ? "null" : sessionHit.Name)}.",
            @event: "probe.smoke.retrieve-by-name.session-hit",
            props: new { name = sessionModelName, type = sessionModelType.ToString(), found = sessionHit is not null });

        // 分支 2:search_path 中有但未 load;触真磁盘检索(env 未设则跳过)。
        var searchName = Environment.GetEnvironmentVariable("CTK_HOST_RETRIEVE_BY_NAME_SEARCH");
        if (!string.IsNullOrWhiteSpace(searchName))
        {
            var searchHit = session.Models.RetrieveByName(searchName, CreoModelType.Part);
            Log($"ctk.probe.smoke.model: retrieve-by-name search-hit result={(searchHit is null ? "null" : searchHit.Name)}.",
                @event: "probe.smoke.retrieve-by-name.search-hit",
                props: new { name = searchName, found = searchHit is not null });
        }
        else
        {
            Log("ctk.probe.smoke.model: retrieve-by-name search-hit skipped; CTK_HOST_RETRIEVE_BY_NAME_SEARCH 未设。",
                @event: "probe.smoke.retrieve-by-name.search-hit.skipped",
                props: new { reason = "env.CTK_HOST_RETRIEVE_BY_NAME_SEARCH.unset" });
        }

        // 分支 3:名字既不在会话也不在 search_path;RetrieveByName 应返 null 不抛。
        var missName = Environment.GetEnvironmentVariable("CTK_HOST_RETRIEVE_BY_NAME_MISS")
            ?? "__CTK_NONEXISTENT_MODEL__";
        var missResult = session.Models.RetrieveByName(missName, CreoModelType.Part);
        Log($"ctk.probe.smoke.model: retrieve-by-name miss result={(missResult is null ? "null" : "unexpected-non-null")}.",
            @event: "probe.smoke.retrieve-by-name.miss",
            props: new { name = missName, foundUnexpectedly = missResult is not null });
    }

    /// <summary>α-mass-csys smoke:验 CreoSolid.GetMassProperty(csys) 新重载真机路径。
    /// 先跑无参默认版(向后兼容路径),再枚举 solid 现有 csys 取第一个名字跑有参版(新 native 路径),
    /// 硬门 mass/volume>0 证明 ProSolidMassPropertyGet(csys_name, mp) 真在 native 执行且 marshalling 正确。</summary>
    private static void RunMassPropertyCsysSmoke(CreoModel model)
    {
        if (model is not CreoSolid solid)
        {
            Log("ctk.probe.smoke.model: mass-property-csys smoke skipped; model not a solid.",
                @event: "probe.smoke.mass-property-csys.skipped",
                props: new { reason = "model.not.solid" });
            return;
        }

        // 分支 1:无参默认版(bridge 内传 empty ProName,对齐 native 默认坐标系语义)。
        try
        {
            var defaultMp = solid.GetMassProperty();
            Log($"ctk.probe.smoke.model: mass-property default vol={defaultMp.Volume:F4} mass={defaultMp.Mass:F4} density={defaultMp.Density:F4}.",
                @event: "probe.smoke.mass-property-csys.default.ok",
                props: new
                {
                    volume = defaultMp.Volume,
                    mass = defaultMp.Mass,
                    density = defaultMp.Density,
                    surfaceArea = defaultMp.SurfaceArea,
                    cogX = defaultMp.CenterOfGravityX,
                    cogY = defaultMp.CenterOfGravityY,
                    cogZ = defaultMp.CenterOfGravityZ,
                });
        }
        catch (Exception ex)
        {
            Log($"ctk.probe.smoke.model: mass-property default failed {ex.GetType().Name}: {ex.Message}",
                @event: "probe.smoke.mass-property-csys.default.failed",
                props: new { exceptionType = ex.GetType().Name, message = ex.Message });
        }

        // 分支 2:有参版(bridge 内传具体 csys 名字给 native)——先枚举 solid 现有 csys 取第一个。
        var csysItems = solid.ListCsys();
        if (csysItems.Count == 0)
        {
            Log("ctk.probe.smoke.model: mass-property-csys named skipped; solid has no csys to reference.",
                @event: "probe.smoke.mass-property-csys.named.skipped",
                props: new { reason = "solid.no.csys" });
            return;
        }

        string? csysName = null;
        if (csysItems[0] is CreoCsys firstCsys)
        {
            csysName = firstCsys.GetName();
        }
        if (string.IsNullOrWhiteSpace(csysName))
        {
            Log("ctk.probe.smoke.model: mass-property-csys named skipped; first csys has no readable name.",
                @event: "probe.smoke.mass-property-csys.named.skipped",
                props: new { reason = "first.csys.no.name" });
            return;
        }

        try
        {
            var namedMp = solid.GetMassProperty(csysName);
            Log($"ctk.probe.smoke.model: mass-property named csys={csysName} vol={namedMp.Volume:F4} mass={namedMp.Mass:F4} density={namedMp.Density:F4}.",
                @event: "probe.smoke.mass-property-csys.named.ok",
                props: new
                {
                    csysName,
                    volume = namedMp.Volume,
                    mass = namedMp.Mass,
                    density = namedMp.Density,
                    surfaceArea = namedMp.SurfaceArea,
                    cogX = namedMp.CenterOfGravityX,
                    cogY = namedMp.CenterOfGravityY,
                    cogZ = namedMp.CenterOfGravityZ,
                });
        }
        catch (Exception ex)
        {
            Log($"ctk.probe.smoke.model: mass-property named csys={csysName} failed {ex.GetType().Name}: {ex.Message}",
                @event: "probe.smoke.mass-property-csys.named.failed",
                props: new { csysName, exceptionType = ex.GetType().Name, message = ex.Message });
        }
    }

    /// <summary>α-geometry smoke:验 Bridge 补实现的 Layer + Quilt 域 4 API(commit 9dc003b)真机 marshalling,
    /// 及后续 Edge / Axis / Curve 域扩容,并叠加纯 L3 DatumPlane 复合视图(无新 Bridge)证组合契约。
    /// Layer 分支:model.Layers.List() → 每 layer.ListItems()(验 ModelLayerList + LayerItemsList);
    /// Quilt 分支:solid.ListQuilts() → 每 quilt.GetVolume() / IsBackupGeometry() / ListSurfaces()
    /// (验 QuiltVolumeEval + QuiltIsBackupGeometry + QuiltSurfaceList);
    /// DatumPlane 分支:solid.ListDatumPlanes()(纯 L3 组合 ListDatumPlaneFeatures + Surface 过滤,无新 native)。
    /// 任一域 fixture 空则该分支 log 空 count(不阻塞另一个)。</summary>
    private static void RunGeometryBridgeSmoke(CreoModel model)
    {
        // Layer 分支:所有 CreoModel 子类都可能有 layer(不限 Solid)
        try
        {
            var layers = model.Layers.List();
            int totalItems = 0;
            foreach (var layer in layers)
            {
                totalItems += layer.ListItems().Count;
            }
            Log($"ctk.probe.smoke.model: geometry layers count={layers.Count} totalItems={totalItems}.",
                @event: "probe.smoke.geometry.layer.ok",
                props: new { layerCount = layers.Count, totalItems });
        }
        catch (Exception ex)
        {
            Log($"ctk.probe.smoke.model: geometry layer smoke failed {ex.GetType().Name}: {ex.Message}",
                @event: "probe.smoke.geometry.layer.failed",
                props: new { exceptionType = ex.GetType().Name, message = ex.Message });
        }

        // Surface + Edge 分支:通过 solid.ListSurfaces() → first surface.ListEdges() → 每 edge Type/Length/NeighborSurfaces
        // 验 Bridge 补实现的 EdgeTypeGet / EdgeLengthGet / EdgeNeighborSurfacesGet + SurfaceEdgeList(嵌套 Contour visit)
        if (model is CreoSolid solidForEdges)
        {
            try
            {
                var surfaces = solidForEdges.ListSurfaces();
                var firstSurface = surfaces.OfType<CreoSurface>().FirstOrDefault();
                if (firstSurface is null)
                {
                    Log("ctk.probe.smoke.model: geometry edge smoke skipped; no surface.",
                        @event: "probe.smoke.geometry.edge.skipped",
                        props: new { reason = "solid.no.surface" });
                }
                else
                {
                    var edges = firstSurface.ListEdges();
                    int typeReads = 0, lengthReads = 0, neighborReads = 0;
                    foreach (var edge in edges)
                    {
                        if (edge.GetGeometryType() is not null) typeReads++;
                        if (edge.GetLength() is not null) lengthReads++;
                        var (f1, f2) = edge.GetNeighborSurfaces();
                        if (f1 is not null || f2 is not null) neighborReads++;
                    }
                    Log($"ctk.probe.smoke.model: geometry surface#{firstSurface.Id} edges count={edges.Count} typeReads={typeReads} lengthReads={lengthReads} neighborReads={neighborReads}.",
                        @event: "probe.smoke.geometry.edge.ok",
                        props: new { surfaceId = firstSurface.Id, edgeCount = edges.Count, typeReads, lengthReads, neighborReads });
                }
            }
            catch (Exception ex)
            {
                Log($"ctk.probe.smoke.model: geometry edge smoke failed {ex.GetType().Name}: {ex.Message}",
                    @event: "probe.smoke.geometry.edge.failed",
                    props: new { exceptionType = ex.GetType().Name, message = ex.Message });
            }
        }

        // Axis 分支:solid.ListAxes → 每 axis.GetLineData() / GetOwnerSurface()
        // 验 Bridge 补实现的 AxisLineDataGet(ProGeomitemdata union 解 ProCurvedata line 抽 end1/end2)
        // + AxisOwnerSurfaceGet(ProAxisSurfaceGet + ProSurfaceIdGet 反查)
        if (model is CreoSolid solidForAxis)
        {
            try
            {
                var axes = solidForAxis.ListAxes();
                int lineDataReads = 0, ownerReads = 0;
                foreach (var item in axes)
                {
                    if (item is not CreoAxis axis) continue;
                    if (axis.GetLineData() is not null) lineDataReads++;
                    if (axis.GetOwnerSurface() is not null) ownerReads++;
                }
                Log($"ctk.probe.smoke.model: geometry axes count={axes.Count} lineDataReads={lineDataReads} ownerReads={ownerReads}.",
                    @event: "probe.smoke.geometry.axis.ok",
                    props: new { axisCount = axes.Count, lineDataReads, ownerReads });
            }
            catch (Exception ex)
            {
                Log($"ctk.probe.smoke.model: geometry axis smoke failed {ex.GetType().Name}: {ex.Message}",
                    @event: "probe.smoke.geometry.axis.failed",
                    props: new { exceptionType = ex.GetType().Name, message = ex.Message });
            }
        }

        // Curve 分支:model.Items.List(Curve) → 每 curve.GetLength / GetTypeCode
        // 验 Bridge 补实现的 CurveLengthGet / CurveTypeGet + SolidCurveList 新加(遍历 feature + PRO_CURVE geomitem visit)
        try
        {
            var curves = model.Items.List(CreoModelItemType.Curve);
            int lengthReads = 0, typeReads = 0;
            foreach (var item in curves)
            {
                if (item is not CreoCurve curve) continue;
                if (curve.GetLength() is not null) lengthReads++;
                if (curve.GetGeometryType() is not null) typeReads++;
            }
            Log($"ctk.probe.smoke.model: geometry curves count={curves.Count} lengthReads={lengthReads} typeReads={typeReads}.",
                @event: "probe.smoke.geometry.curve.ok",
                props: new { curveCount = curves.Count, lengthReads, typeReads });
        }
        catch (Exception ex)
        {
            Log($"ctk.probe.smoke.model: geometry curve smoke failed {ex.GetType().Name}: {ex.Message}",
                @event: "probe.smoke.geometry.curve.failed",
                props: new { exceptionType = ex.GetType().Name, message = ex.Message });
        }

        // DatumPlane 分支:纯 L3 复合视图(无新 Bridge)——CreoSolid.ListDatumPlanes() 内部走
        // CreoFeatures.ListDatumPlaneFeatures(PRO_FEAT_DATUM 筛) → 每 datum feat ListGeomItems(Surface)
        // → CreoSurface.GetSurfaceType 筛 PRO_SRF_PLANE。fixture(exercise4.prt)期望有默认 DTM1/2/3 三面,
        // 但不硬门具体数量(承认 fixture gap,类比 layer/quilt)。仅 Solid 有 datum plane feature 域。
        if (model is CreoSolid solidForDatumPlane)
        {
            try
            {
                var datumPlanes = solidForDatumPlane.ListDatumPlanes();
                Log($"ctk.probe.smoke.model: geometry datum planes count={datumPlanes.Count}.",
                    @event: "probe.smoke.geometry.datumplane.ok",
                    props: new { datumPlaneCount = datumPlanes.Count });
            }
            catch (Exception ex)
            {
                Log($"ctk.probe.smoke.model: geometry datum plane smoke failed {ex.GetType().Name}: {ex.Message}",
                    @event: "probe.smoke.geometry.datumplane.failed",
                    props: new { exceptionType = ex.GetType().Name, message = ex.Message });
            }
        }

        // Quilt 分支:仅 Solid 有 quilt 域
        if (model is not CreoSolid solid)
        {
            Log("ctk.probe.smoke.model: geometry quilt smoke skipped; model not a solid.",
                @event: "probe.smoke.geometry.quilt.skipped",
                props: new { reason = "model.not.solid" });
            return;
        }
        try
        {
            var quilts = solid.ListQuilts();
            int volumeReads = 0;
            int backupReads = 0;
            int surfaceReads = 0;
            foreach (var item in quilts)
            {
                if (item is not CreoQuilt quilt) continue;
                if (quilt.GetVolume() is not null) volumeReads++;
                if (quilt.IsBackupGeometry() is not null) backupReads++;
                surfaceReads += quilt.ListSurfaces().Count;
            }
            Log($"ctk.probe.smoke.model: geometry quilts count={quilts.Count} volumeReads={volumeReads} backupReads={backupReads} surfaceReads={surfaceReads}.",
                @event: "probe.smoke.geometry.quilt.ok",
                props: new { quiltCount = quilts.Count, volumeReads, backupReads, surfaceReads });
        }
        catch (Exception ex)
        {
            Log($"ctk.probe.smoke.model: geometry quilt smoke failed {ex.GetType().Name}: {ex.Message}",
                @event: "probe.smoke.geometry.quilt.failed",
                props: new { exceptionType = ex.GetType().Name, message = ex.Message });
        }
    }

    private static int ReadIntEnv(string name, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        if (!int.TryParse(raw, out var parsed)) return defaultValue;
        if (parsed < min) return min;
        if (parsed > max) return max;
        return parsed;
    }

    private static CreoModelType ReadSmokeModelType(CreoModelType fallback)
    {
        var value = Environment.GetEnvironmentVariable("CTK_HOST_SMOKE_MODEL_TYPE");
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (Enum.TryParse<CreoModelType>(value, ignoreCase: true, out var parsed)) return parsed;
        throw new InvalidOperationException($"Unsupported CTK_HOST_SMOKE_MODEL_TYPE '{value}'.");
    }

    private static bool IsEnvEnabled(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static void Log(string message, string @event, object? props = null)
        => CreoAppLog.Info(message, @event: @event, module: "HostProbe", props: props);
}
