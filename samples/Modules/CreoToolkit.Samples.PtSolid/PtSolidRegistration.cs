using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtSolid;

/// <summary>
/// Solid 直接暴露的 read-only 切片(pt.solid.* 前缀)。
/// 首条 pt.solid.failed-features 覆盖 ProSolidFailedfeaturesList 的 ProArray-of-int 读回真机点位。
/// </summary>
public static class PtSolidRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtSolidFailedFeatures(app, modelName, modelType);
        RegisterPtSolidFamilyTable(app, modelName, modelType);
    }

    private static void RegisterPtSolidFailedFeatures(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // ProSolidFailedfeaturesList 直绑 (ProArray-of-int DATA)。
        // 覆盖 CreoSolid.ListFailedFeatureIds → CreoNativeBridge.SolidFailedFeatures 读回真机点位。
        app.Command("pt.solid.failed-features", "PT_SOLID_FAILED_FEATURES", "PT_SOLID_FAILED_FEATURES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var solid = (CreoSolid)model;
            var ids = solid.ListFailedFeatureIds();
            var preview = ids.Count > 0 ? string.Join(",", ids.Take(5)) : "(none)";
            ctx.Messages.Info($"{model.FullName} failed features={ids.Count} first={preview}");
        });
    }

    private static void RegisterPtSolidFamilyTable(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.solid.family-table", "PT_SOLID_FAMILY_TABLE", "PT_SOLID_FAMILY_TABLE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"{model.FullName} is not a solid");
                return;
            }

            var status = solid.CheckFamilyTable();
            var names = solid.ListFamilyInstanceNames();
            ctx.Messages.Info(
                $"family-table model={model.FullName} status={status?.ToString() ?? "unavailable"} " +
                $"instances={names.Count} names={string.Join(",", names.Take(20))}");
            ctx.Messages.Info("family-table complete");
        });
    }
}
