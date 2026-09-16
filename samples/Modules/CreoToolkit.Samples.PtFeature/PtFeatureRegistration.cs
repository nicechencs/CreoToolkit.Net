using System.Linq;
using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtFeature;

public static class PtFeatureRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtFeatureWalk(app, modelName, modelType);
        RegisterPtFeatureList(app, modelName, modelType);
        RegisterPtFeatureDelete(app, modelName, modelType);
        }

    private static void RegisterPtFeatureWalk(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.feature.walk", "PT_FEATURE_WALK", "PT_FEATURE_WALK_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            var feature = session.Features.First(model);
            if (feature is null)
            {
                ctx.Messages.Info($"No feature found on {model.FullName}");
                return;
            }

            using var tree = feature.ExtractElementTree();
            var nodes = tree.Walk().ToList();
            ctx.Messages.Info($"Feature {feature.Id} element nodes={nodes.Count} on {model.FullName}");
        });
    }

    private static void RegisterPtFeatureList(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.feature.list", "PT_FEATURE_LIST", "PT_FEATURE_LIST_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            var features = session.Features.List(model);
            if (features.Count == 0)
            {
                ctx.Messages.Info($"No features on {model.FullName}");
                return;
            }

            var ids = string.Join(", ", features.Select(f => f.Id));
            ctx.Messages.Info($"Features on {model.FullName}: count={features.Count} ids=[{ids}]");
        });
    }

    private static void RegisterPtFeatureDelete(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.feature.delete", "PT_FEATURE_DELETE", "PT_FEATURE_DELETE_HELP", ctx =>
        {
            if (!ctx.RequireSession(out var session)) return;

            var model = session.Models.Retrieve(modelName, modelType);
            var feature = session.Features.First(model);
            if (feature is null)
            {
                ctx.Messages.Info($"No feature found on {model.FullName}");
                return;
            }

            var id = feature.Id;
            feature.Delete();
            ctx.Messages.Info($"Deleted feature {id} on {model.FullName}");
        });

    }
}
