using CreoToolkit.App;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Samples.PtDimension;

/// <summary>
/// 形位公差(GTol)创建闭环命令。
/// 在 exercise4.prt 上创建 FLATNESS "0.05" → 读回断言 type/value → 删除收尾。
/// </summary>
internal static class PtGtolRegistration
{
    internal static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        RegisterPtGtolAttachMakeDim(app, modelName, modelType);
        app.Command("pt.gtol.create-roundtrip", "PT_GTOL_CREATE_ROUNDTRIP",
            "PT_GTOL_CREATE_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoSolid solid)
                {
                    ctx.Messages.Info($"model {modelName} is not a solid, cannot create gtol");
                    return;
                }

                try
                {
                    // 创建 FLATNESS "0.05" (location = 原点)
                    var item = solid.CreateGtol(CreoGtolType.Flatness, "0.05", 0, 0, 0);
                    ctx.Messages.Info($"gtol_created id={item.Id}");

                    // 读回验证
                    var info = solid.ReadGtol(item.Id);
                    var typeOk = info.Type == CreoGtolType.Flatness;
                    var valueOk = info.ValueString == "0.05";

                    ctx.Messages.Info(
                        $"gtol_readback type={info.Type} rawType={info.RawType} " +
                        $"valueString=\"{info.ValueString}\" " +
                        $"typeMatch={typeOk} valueMatch={valueOk}");

                    if (!typeOk || !valueOk)
                    {
                        ctx.Messages.Info("gtol_roundtrip ASSERTION FAILED");
                    }

                    // 删除收尾
                    solid.DeleteGtol(item.Id);
                    ctx.Messages.Info("gtol_deleted");

                    ctx.Messages.Info("gtol-create-roundtrip complete");
                }
                catch (CreoException ex)
                {
                    ctx.Messages.Info(
                        $"gtol-create-roundtrip failed: rc={ex.ErrorCode} {ex.Message}");
                }
            });
    }

    private static void RegisterPtGtolAttachMakeDim(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // CreoModel.PrepareGtolAttachAndReadMakeDim → 组合流程(Alloc + FreeSet +
        // ProGtolAttachMakeDimGet + Free),读回 attach 的 make-dim 信息(annotation plane /
        // attach count / senses / location xyz)。
        app.Command("pt.gtol.attach-make-dim", "PT_GTOL_ATTACH_MAKE_DIM",
            "PT_GTOL_ATTACH_MAKE_DIM_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                if (model is not CreoSolid)
                {
                    ctx.Messages.Info($"model {modelName} is not a solid, cannot prepare gtol attach");
                    return;
                }

                try
                {
                    var info = model.PrepareGtolAttachAndReadMakeDim(0, 0, 0);
                    if (info is null)
                    {
                        ctx.Messages.Info(
                            $"gtol-attach-make-dim on {model.FullName}: -> (null, model may not be in session)");
                        ctx.Messages.Info("gtol-attach-make-dim complete");
                        return;
                    }

                    var planeTag = info.AnnotationPlane is { } p ? $"{p.Type}#{p.Id}" : "none";
                    var loc = info.Location is { } l ? $"({l.X:F3},{l.Y:F3},{l.Z:F3})" : "(none)";
                    ctx.Messages.Info(
                        $"gtol-attach-make-dim on {model.FullName}: " +
                        $"plane={planeTag}; orient={info.OrientHint}; " +
                        $"pairs={info.AttachmentCount}; senses={info.Senses.Count}; loc={loc}");
                    ctx.Messages.Info("gtol-attach-make-dim complete");
                }
                catch (CreoException ex)
                {
                    // FREE attach(flat annotation plane + xyz)不带 make-dim 信息
                    // (make-dim 需要 attach 到 geometry pairs),BadInputs 是业务事实,
                    // 不阻断汇总。
                    ctx.Messages.Info(
                        $"gtol-attach-make-dim on {model.FullName}: -> " +
                        $"(native rc={ex.ErrorCode}, FREE attach has no make-dim info)");
                    ctx.Messages.Info("gtol-attach-make-dim complete");
                }
            });
    }
}
