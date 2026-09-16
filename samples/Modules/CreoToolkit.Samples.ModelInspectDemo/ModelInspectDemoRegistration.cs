using System.Linq;
using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.ModelInspectDemo;

/// <summary>
/// 模型巡检 sample(read-only):演示用 CreoToolkit.Sdk 扫描特征/参数 + 纯 .NET 几何工具,落地 3 个命令:
/// <list type="bullet">
///   <item><c>pt.model-inspect.geometry-snippet</c>:演示 <see cref="InspectGeometry"/> 纯 .NET 几何算法
///         (向量平行、点-线距离、三角形面积、点集合按容差移除)。</item>
///   <item><c>pt.model-inspect.feature-summary</c>:遍历当前模型的全部特征 + 抽取首特征元素树节点数。</item>
///   <item><c>pt.model-inspect.param-categorize</c>:扫描模型参数并按前缀分桶统计。</item>
/// </list>
/// </summary>
public static class ModelInspectDemoRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterGeometrySnippet(app);
        RegisterFeatureSummary(app, modelName, modelType);
        RegisterParamCategorize(app, modelName, modelType);

    }

    private static void RegisterGeometrySnippet(CreoAppBuilder app)
    {
        // 无 Creo 依赖:纯 .NET 几何工具演示。
        app.Command("pt.model-inspect.geometry-snippet",
            "PT_MODEL_INSPECT_GEOMETRY_SNIPPET",
            "PT_MODEL_INSPECT_GEOMETRY_SNIPPET_HELP", ctx =>
        {
            // 案例 1:三点共线检测(等距共线 → true)。
            var p0 = new CreoVec3(0, 0, 0);
            var p1 = new CreoVec3(1, 0, 0);
            var p2 = new CreoVec3(2, 0, 0);
            bool collinear = InspectGeometry.ArePointsCollinear(p0, p1, p2);

            // 案例 2:线段方向 vs 平面法向(z 方向线段 vs UnitZ 平面法向 → 平行 = "normal to plane")。
            var segA = new CreoVec3(0, 0, 0);
            var segB = new CreoVec3(0, 0, 5);
            bool normalToTop = InspectGeometry.IsSegmentNormalToPlane(segA, segB, CreoVec3.UnitZ);

            // 案例 3:三角形面积 + 点到直线距离。
            var t1 = new CreoVec3(0, 0, 0);
            var t2 = new CreoVec3(3, 0, 0);
            var t3 = new CreoVec3(0, 4, 0);
            double area = InspectGeometry.TriangleArea(t1, t2, t3);
            double distP2ToLine = InspectGeometry.DistanceFromPointToLine(t3, t1, t2);

            // 案例 4:点集合按容差移除。
            IReadOnlyList<CreoVec3> bag = new[] { p0, p1, p2 };
            var trimmed = InspectGeometry.RemovePoint(bag, p1);

            ctx.Messages.Info(
                $"geometry-snippet: collinear={collinear}; normal-to-top={normalToTop}; " +
                $"area(3-4-5)={area:F3}; dist={distP2ToLine:F3}; bag={bag.Count}->{trimmed.Count}");
        });
    }

    private static void RegisterFeatureSummary(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // 批量扫描特征 read-only:当前 L3 未暴露 ProFeatureTypeGet/Status/Resume,本切片只演示扫描 + 元素树节点统计。
        app.Command("pt.model-inspect.feature-summary",
            "PT_MODEL_INSPECT_FEATURE_SUMMARY",
            "PT_MODEL_INSPECT_FEATURE_SUMMARY_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var features = ctx.Session!.Features.List(model);
            if (features.Count == 0)
            {
                ctx.Messages.Info($"feature-summary on {model.FullName}: no features");
                return;
            }

            // 首特征元素树节点数:演示元素树抽取+遍历的资源生命周期(using = Dispose 经 dispatcher 释放)。
            int firstNodes;
            using (var tree = features[0].ExtractElementTree())
                firstNodes = tree.Walk().Count();

            // 按 id 段分桶演示批量分类模式。
            var bucketLo = features.Count(f => f.Id < 100);
            var bucketHi = features.Count(f => f.Id >= 100);

            ctx.Messages.Info(
                $"feature-summary on {model.FullName}: total={features.Count}; " +
                $"id<100={bucketLo}; id>=100={bucketHi}; first-feature-id={features[0].Id}; first-tree-nodes={firstNodes}");
        });
    }

    private static void RegisterParamCategorize(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // 按命名前缀分桶处理参数。
        app.Command("pt.model-inspect.param-categorize",
            "PT_MODEL_INSPECT_PARAM_CATEGORIZE",
            "PT_MODEL_INSPECT_PARAM_CATEGORIZE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var parameters = model.Parameters.List();
            if (parameters.Count == 0)
            {
                ctx.Messages.Info($"param-categorize on {model.FullName}: no parameters");
                return;
            }

            int ctkBucket = 0, pipeBucket = 0, cfgBucket = 0, otherBucket = 0;
            foreach (var p in parameters)
            {
                if (p.Name.StartsWith("CTK_", StringComparison.Ordinal)) ctkBucket++;
                else if (p.Name.StartsWith("PIPE_", StringComparison.Ordinal)) pipeBucket++;
                else if (p.Name.StartsWith("CFG_", StringComparison.Ordinal)) cfgBucket++;
                else otherBucket++;
            }

            ctx.Messages.Info(
                $"param-categorize on {model.FullName}: total={parameters.Count}; " +
                $"CTK_={ctkBucket}; PIPE_={pipeBucket}; CFG_={cfgBucket}; other={otherBucket}");
        });
    }
}
