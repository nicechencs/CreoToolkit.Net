using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtLayer;

/// <summary>
/// Layer 直接暴露的 read-only 切片(pt.layer.* 前缀)。
/// 首条 pt.layer.contains-item 覆盖 ProLayerItemsPopulate + ProArrayScope 读回真机点位。
/// </summary>
public static class PtLayerRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtLayerContainsItem(app, modelName, modelType);
    }

    private static void RegisterPtLayerContainsItem(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // ProLayerItemsPopulate 直绑 (ProArray-of-ProLayerItem DATA + ProArrayScope 释放)。
        // 覆盖 CreoLayers.ContainsFeature → LayerContainsItemImpl 读回真机点位。
        // 无 layer 或无 feature 时输出 skipped:此时 native 走的是 ProMdlLayerGet 早退 (IsNotFound),
        // Populate 未触发;fact 上报 skipped 让运维知会,不算路径覆盖失败。
        app.Command("pt.layer.contains-item", "PT_LAYER_CONTAINS_ITEM", "PT_LAYER_CONTAINS_ITEM_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var session = ctx.Session
                ?? throw new InvalidOperationException("No Creo session for layer contains probe.");
            var layerNames = model.Layers.ListNames();
            var first = session.Features.First(model);
            if (layerNames.Count == 0 || first is null)
            {
                ctx.Messages.Info(
                    $"{model.FullName} layer contains=skipped layers={layerNames.Count} feature={(first is null ? "(none)" : first.Id.ToString())}");
                return;
            }
            var layerName = layerNames[0];
            var contains = model.Layers.ContainsFeature(layerName, first);
            ctx.Messages.Info(
                $"{model.FullName} layer contains={contains} layer={layerName} feature={first.Id}");
        });
    }
}
