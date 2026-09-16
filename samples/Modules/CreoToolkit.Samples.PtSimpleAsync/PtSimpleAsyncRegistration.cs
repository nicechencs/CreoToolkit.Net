using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtSimpleAsync;

public static class PtSimpleAsyncRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        // pt_simple_async: hosted 形态不启动/结束 Creo，只保留 retrieve + save 业务切片。
        app.Command("pt.simple_async.retrieve-save", "PT_SIMPLE_ASYNC_RETRIEVE_SAVE",
            "PT_SIMPLE_ASYNC_RETRIEVE_SAVE_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                model.Save();
                ctx.Messages.Info($"Saved model: {model.FullName}");
            });

    }
}
