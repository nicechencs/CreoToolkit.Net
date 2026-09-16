using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.ElemTreeWalk;

/// <summary>
/// pt.element.tree.walk 诊断命令: 遍历模型全部特征的元素树(深度模式),输出特征类型、节点值、父子关系。
/// </summary>
public static class ElemTreeWalkRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);

        app.Command("pt.element.tree.walk",
            "PT_ELEM_TREE_WALK",
            "PT_ELEM_TREE_WALK_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var features = ctx.Session!.Features.List(model);
            if (features.Count == 0)
            {
                ctx.Messages.Info($"element-tree-walk on {model.FullName}: no features");
                return;
            }

            foreach (var feature in features)
            {
                var info = feature.GetInfo();
                var featType = info?.FeatureType ?? -1;
                ctx.Messages.Info($"feat_type={featType} id={feature.Id}");

                var parentIds = feature.GetParentIds();
                foreach (var pid in parentIds)
                    ctx.Messages.Info($"parent_feat_id={pid} child_feat_id={feature.Id}");

                var childIds = feature.GetChildIds();
                foreach (var cid in childIds)
                    ctx.Messages.Info($"child_feat_id={cid} parent_feat_id={feature.Id}");

                using var tree = feature.ExtractElementTreeDeep();
                var nodes = tree.Walk().ToList();
                ctx.Messages.Info($"node_count={nodes.Count}");

                foreach (var node in nodes)
                {
                    var valStr = node.ValueKind switch
                    {
                        CreoElementValueKind.Integer => $"int={node.IntValue}",
                        CreoElementValueKind.Double => $"dbl={node.DoubleValue:G}",
                        CreoElementValueKind.String => $"str={node.StringValue}",
                        CreoElementValueKind.Boolean => $"bool={node.BoolValue}",
                        CreoElementValueKind.Reference => "ref",
                        CreoElementValueKind.Transform => "xform",
                        _ => "none",
                    };
                    ctx.Messages.Info($"elem_tree node elemId={node.ElementId} level={node.Level} {valStr}");
                }
            }

            ctx.Messages.Info($"element-tree-walk complete: {features.Count} features on {model.FullName}");
        });

        app.Command("pt.element.datum.create",
            "PT_ELEM_DATUM_CREATE",
            "PT_ELEM_DATUM_CREATE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            // 找首个平面 surface(pro_srf_type PLANE=34)作偏移参照
            var solid = (CreoSolid)model;
            var plane = solid.ListSurfaces()
                .OfType<CreoSurface>()
                .FirstOrDefault(s => s.GetSurfaceType() == 34);
            if (plane is null)
            {
                ctx.Messages.Info($"datum-plane-create on {model.FullName}: no planar surface");
                return;
            }
            ctx.Messages.Info($"datum_ref_surface_id={plane.Id}");

            const double offset = 10.0;
            var feature = ctx.Session!.Features.CreateDatumPlane(model, plane, offset);
            ctx.Messages.Info($"datum_created id={feature.Id}");

            var info = feature.GetInfo();
            ctx.Messages.Info($"datum_feat_type={info?.FeatureType ?? -1}");

            solid.Regenerate();

            // 回读元素树验证 offset(PRO_E_DTMPLN_CONSTR_REF_OFFSET=414)
            using (var tree = feature.ExtractElementTreeDeep())
            {
                var offsetNodes = tree.Walk()
                    .Where(n => n.ElementId == 414 && n.ValueKind == CreoElementValueKind.Double)
                    .ToList();
                ctx.Messages.Info(offsetNodes.Count == 0
                    ? "datum_offset_readback=missing"
                    : $"datum_offset_readback={offsetNodes[0].DoubleValue:G}");
            }

            feature.Delete();
            ctx.Messages.Info($"datum-plane-create complete on {model.FullName}");
        });

        app.Command("pt.element.hole.create",
            "PT_ELEM_HOLE_CREATE",
            "PT_ELEM_HOLE_CREATE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            // 找三个互相垂直的平面 surface:放置面 + 两个线性参照
            var solid = (CreoSolid)model;
            var planes = solid.ListSurfaces()
                .OfType<CreoSurface>()
                .Where(s => s.GetSurfaceType() == 34)
                .Select(s => (Surface: s, Plane: s.GetPlane()))
                .Where(p => p.Plane is not null)
                .Select(p => (p.Surface, Normal: p.Plane!.Value.Normal.Normalize()))
                .ToList();

            var placement = planes.FirstOrDefault();
            var r1 = planes.FirstOrDefault(p => Math.Abs(p.Normal.Dot(placement.Normal)) < 0.02);
            var r2 = planes.FirstOrDefault(p => Math.Abs(p.Normal.Dot(placement.Normal)) < 0.02
                                                && Math.Abs(p.Normal.Dot(r1.Normal)) < 0.02);
            if (placement.Surface is null || r1.Surface is null || r2.Surface is null)
            {
                ctx.Messages.Info($"hole-create on {model.FullName}: no orthogonal plane triple");
                return;
            }
            ctx.Messages.Info(
                $"hole_refs placement={placement.Surface.Id} ref1={r1.Surface.Id} ref2={r2.Surface.Id}");

            var standard = string.Equals(
                Environment.GetEnvironmentVariable("CTK_HOLE_KIND"),
                "standard",
                StringComparison.OrdinalIgnoreCase);
            CreoFeature feature;
            if (standard)
            {
                var series = int.TryParse(
                    Environment.GetEnvironmentVariable("CTK_STANDARD_HOLE_THREAD_SERIES_INDEX"),
                    out var configuredSeries) ? configuredSeries : 0;
                var size = int.TryParse(
                    Environment.GetEnvironmentVariable("CTK_STANDARD_HOLE_SCREW_SIZE_INDEX"),
                    out var configuredSize) ? configuredSize : 0;
                feature = new StandardHoleBuilder()
                    .Kind(StandardHoleKind.Tapped)
                    .ThreadSeriesIndex(series)
                    .ScrewSizeIndex(size)
                    .Diameter(5.0)
                    .DrillDepth(10.0)
                    .ThreadDepth(8.0)
                    .PlacementPlane(placement.Surface)
                    .LinearReference(r1.Surface, 15.0)
                    .LinearReference(r2.Surface, 15.0)
                    .Build(model);
            }
            else
            {
                feature = new HoleBuilder()
                    .Diameter(5.0).Depth(10.0)
                    .PlacementPlane(placement.Surface)
                    .LinearReference(r1.Surface, 15.0)
                    .LinearReference(r2.Surface, 15.0)
                    .Build(model);
            }
            ctx.Messages.Info($"hole_created id={feature.Id} kind={(standard ? "standard" : "straight")}");

            solid.Regenerate();

            // 读写闭环:HoleSpecReader 读回断言素材
            var spec = HoleSpecReader.FromFeature(feature);
            ctx.Messages.Info(spec is null
                ? "hole_readback=missing"
                : $"hole_readback diameter={spec.Value.Diameter:G} depth={spec.Value.Depth:G} "
                  + $"type={spec.Value.Type} depthType={spec.Value.DepthType}");

            feature.Delete();
            ctx.Messages.Info($"hole-create complete on {model.FullName}");
        });

        app.Command("pt.element.hole.redefine",
            "PT_ELEM_HOLE_REDEFINE",
            "PT_ELEM_HOLE_REDEFINE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            // 复用建孔序列(diameter=5, depth=10)
            var solid = (CreoSolid)model;
            var planes = solid.ListSurfaces()
                .OfType<CreoSurface>()
                .Where(s => s.GetSurfaceType() == 34)
                .Select(s => (Surface: s, Plane: s.GetPlane()))
                .Where(p => p.Plane is not null)
                .Select(p => (p.Surface, Normal: p.Plane!.Value.Normal.Normalize()))
                .ToList();

            var placement = planes.FirstOrDefault();
            var r1 = planes.FirstOrDefault(p => Math.Abs(p.Normal.Dot(placement.Normal)) < 0.02);
            var r2 = planes.FirstOrDefault(p => Math.Abs(p.Normal.Dot(placement.Normal)) < 0.02
                                                && Math.Abs(p.Normal.Dot(r1.Normal)) < 0.02);
            if (placement.Surface is null || r1.Surface is null || r2.Surface is null)
            {
                ctx.Messages.Info($"hole-redefine on {model.FullName}: no orthogonal plane triple");
                return;
            }

            var feature = new HoleBuilder()
                .Diameter(5.0).Depth(10.0)
                .PlacementPlane(placement.Surface)
                .LinearReference(r1.Surface, 15.0)
                .LinearReference(r2.Surface, 15.0)
                .Build(model);
            ctx.Messages.Info($"hole_created id={feature.Id}");

            solid.Regenerate();

            // Redefine 直径 5 → 8
            ctx.Session!.Features.RedefineHoleDiameter(model, feature, 8.0);

            solid.Regenerate();

            // 读回验证
            var spec = HoleSpecReader.FromFeature(feature);
            ctx.Messages.Info(spec is null
                ? "hole_readback=missing"
                : $"hole_redefined diameter={spec.Value.Diameter:G} depth={spec.Value.Depth:G}");

            feature.Delete();
            ctx.Messages.Info("hole-redefine complete");
        });
    }
}
