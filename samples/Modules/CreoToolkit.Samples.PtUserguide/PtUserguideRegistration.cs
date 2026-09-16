using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtUserguide;

public static class PtUserguideRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtUserguideMain(app, modelName, modelType);
        RegisterPtUserguideMessageFormat(app, modelName, modelType);
        RegisterPtUserguideModelLayerScan(app, modelName, modelType);
        RegisterPtUserguideLayerRoundtrip(app, modelName, modelType);
        RegisterPtUserguideLayerDisplayRoundtrip(app, modelName, modelType);
        RegisterPtUserguideLayerFeatureRoundtrip(app, modelName, modelType);
        RegisterPtUserguideWindowRepaint(app, modelName, modelType);

    }

    private static void RegisterPtUserguideMain(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_main/UserGuideMain: 顶层入口目录。
        app.Command("pt.userguide.main", "PT_USERGUIDE_MAIN", "PT_USERGUIDE_MAIN_HELP", ctx =>
        {
            var sections = string.Join(", ", new[]
            {
                "Menus",
                "Messages",
                "Load/Display",
                "Interface",
                "Utilities",
                "Graphics",
                "Feature Creation",
                "Import Feature",
            });
            ctx.Messages.Info($"User Guide main menu sections: {sections}");
        });
    }

    private static void RegisterPtUserguideMessageFormat(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_message/UserMessageDemo: 消息格式化。
        app.Command("pt.userguide.message-format", "PT_USERGUIDE_MESSAGE_FORMAT",
            "PT_USERGUIDE_MESSAGE_FORMAT_HELP", ctx =>
            {
                const int firstInteger = 7;
                const int rangedInteger = 0;
                const double doubleValue = 3.142;
                const string stringValue = "default string";

                var line = FormattableString.Invariant(
                    $"Values entered were {firstInteger}, {rangedInteger}, {doubleValue:0.000}, \"{stringValue}\"");
                ctx.Messages.Info($"User Guide message format: {line}");
            });
    }

    private static void RegisterPtUserguideModelLayerScan(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_model/UserScanLayers: Creo4 原例禁用 layer 逻辑；模型上下文预检。
        app.Command("pt.userguide.model-layer-scan", "PT_USERGUIDE_MODEL_LAYER_SCAN",
            "PT_USERGUIDE_MODEL_LAYER_SCAN_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                var layers = model.Layers.ListNames();
                var preview = layers.Count == 0 ? "none" : string.Join(", ", layers.Take(5));
                ctx.Messages.Info(
                    $"User Guide model layer scan OK: {model.FullName} type={model.Type}; layers={layers.Count}; first={preview}");
            });
    }

    private static void RegisterPtUserguideLayerRoundtrip(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_model/UserLayerCreate: 非交互自动化切片，只验证 create/delete 通道并清理临时 layer。
        app.Command("pt.userguide.layer-roundtrip", "PT_USERGUIDE_LAYER_ROUNDTRIP",
            "PT_USERGUIDE_LAYER_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                var layerName = "CTKRT" + DateTime.UtcNow.ToString("HHmmssfff");
                var created = false;
                var deleted = false;

                try
                {
                    created = model.Layers.Create(layerName);
                    if (!created || !model.Layers.ListNames().Contains(layerName, StringComparer.Ordinal))
                        throw new InvalidOperationException($"Layer {layerName} was not created.");
                }
                finally
                {
                    if (created)
                        deleted = model.Layers.Delete(layerName);
                }

                if (!deleted || model.Layers.ListNames().Contains(layerName, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Layer {layerName} was not deleted.");

                ctx.Messages.Info(
                    $"User Guide layer roundtrip OK: {model.FullName} type={model.Type}; layer={layerName}; created={created}; deleted={deleted}");
            });
    }

    private static void RegisterPtUserguideLayerDisplayRoundtrip(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_model/UserScanLayers: 验证 layer display status get/set，并恢复临时状态。
        app.Command("pt.userguide.layer-display-roundtrip", "PT_USERGUIDE_LAYER_DISPLAY_ROUNDTRIP",
            "PT_USERGUIDE_LAYER_DISPLAY_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                var layerName = "CTKDS" + DateTime.UtcNow.ToString("HHmmssfff");
                var created = false;
                var deleted = false;

                try
                {
                    created = model.Layers.Create(layerName);
                    if (!created)
                        throw new InvalidOperationException($"Layer {layerName} was not created.");

                    var original = model.Layers.GetDisplayStatus(layerName)
                        ?? throw new InvalidOperationException($"Layer {layerName} display status was not readable.");

                    if (!model.Layers.SetDisplayStatus(layerName, CreoLayerDisplayStatus.Blank))
                        throw new InvalidOperationException($"Layer {layerName} display status was not set.");

                    var blank = model.Layers.GetDisplayStatus(layerName);
                    if (blank != CreoLayerDisplayStatus.Blank)
                        throw new InvalidOperationException($"Layer {layerName} display status expected Blank, got {blank}.");

                    if (!model.Layers.SetDisplayStatus(layerName, original))
                        throw new InvalidOperationException($"Layer {layerName} display status was not restored.");
                }
                finally
                {
                    if (created)
                        deleted = model.Layers.Delete(layerName);
                }

                if (!deleted || model.Layers.ListNames().Contains(layerName, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Layer {layerName} was not deleted.");

                ctx.Messages.Info(
                    $"User Guide layer display roundtrip OK: {model.FullName} type={model.Type}; layer={layerName}; deleted={deleted}");
            });
    }

    private static void RegisterPtUserguideLayerFeatureRoundtrip(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_userguide/ptu_model/UserBlank: 非交互切片，验证 ProLayerItemAdd 的 feature item 路径。
        app.Command("pt.userguide.layer-feature-roundtrip", "PT_USERGUIDE_LAYER_FEATURE_ROUNDTRIP",
            "PT_USERGUIDE_LAYER_FEATURE_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;

                var model = session.Models.Retrieve(modelName, modelType);
                var feature = session.Features.First(model)
                    ?? throw new InvalidOperationException($"Model {model.FullName} has no enumerable feature.");
                var layerName = "CTKFI" + DateTime.UtcNow.ToString("HHmmssfff");
                var created = false;
                var deleted = false;

                try
                {
                    created = model.Layers.Create(layerName);
                    if (!created)
                        throw new InvalidOperationException($"Layer {layerName} was not created.");

                    if (!model.Layers.AddFeature(layerName, feature))
                        throw new InvalidOperationException($"Feature {feature.Id} was not added to layer {layerName}.");

                    if (!model.Layers.ContainsFeature(layerName, feature))
                        throw new InvalidOperationException($"Layer {layerName} does not contain feature {feature.Id}.");
                }
                finally
                {
                    if (created)
                        deleted = model.Layers.Delete(layerName);
                }

                if (!deleted || model.Layers.ListNames().Contains(layerName, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Layer {layerName} was not deleted.");

                ctx.Messages.Info(
                    $"User Guide layer feature roundtrip OK: {model.FullName} type={model.Type}; layer={layerName}; feature={feature.Id}; deleted={deleted}");
            });
    }

    private static void RegisterPtUserguideWindowRepaint(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // 多个 ptu_* 示例在模型/绘图变更后调用 ProWindowRepaint(PRO_VALUE_UNUSED) 刷当前窗口。
        app.Command("pt.userguide.window-repaint", "PT_USERGUIDE_WINDOW_REPAINT",
            "PT_USERGUIDE_WINDOW_REPAINT_HELP", ctx =>
            {
                if (!ctx.RequireSession(out var session)) return;

                var repainted = session.Windows.RepaintCurrent();
                var status = repainted ? "current window repainted" : "no repaintable current window; repaint skipped";
                ctx.Messages.Info($"User Guide window repaint OK: {status}");
            });
    }
}
