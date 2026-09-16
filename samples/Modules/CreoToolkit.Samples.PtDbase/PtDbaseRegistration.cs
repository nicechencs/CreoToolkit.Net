using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtDbase;

public static class PtDbaseRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtDbaseRetrieveInfo(app, modelName, modelType);
        RegisterPtDbaseRegenerate(app, modelName, modelType);
        RegisterPtDbaseSave(app, modelName, modelType);
        RegisterPtDbaseErase(app, modelName, modelType);
        RegisterPtDbaseCopy(app, modelName, modelType);
        RegisterPtDbaseOrigin(app, modelName, modelType);
        RegisterPtDbaseCommonName(app, modelName, modelType);
        RegisterPtDbaseDependencies(app, modelName, modelType);
        RegisterPtDbaseDisplay(app, modelName, modelType);
        RegisterPtDbaseOutline(app, modelName, modelType);
        RegisterPtDbaseMassProps(app, modelName, modelType);
        RegisterPtDbaseConfigOptions(app);
        }

    private static void RegisterPtDbaseRetrieveInfo(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlnameRetrieve + name/type 查询切片。
        app.Command("pt.dbase.retrieve-info", "PT_DBASE_RETRIEVE_INFO", "PT_DBASE_RETRIEVE_INFO_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            ctx.Messages.Info($"Retrieved model: {model.FullName} type={model.Type}");
        });
    }

    private static void RegisterPtDbaseRegenerate(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestSolid.c: 迁移 ProSolidRegenerate 的模型级动作切片。
        app.Command("pt.dbase.regenerate", "PT_DBASE_REGENERATE", "PT_DBASE_REGENERATE_HELP", ctx =>
        {
            // Regenerate 在 CreoSolid;modelType 是 Part → 必为 CreoSolid。
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var solid = (CreoSolid)model;
            solid.Regenerate();
            ctx.Messages.Info($"Regenerated model: {solid.FullName}");
        });
    }

    private static void RegisterPtDbaseSave(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProTestModelSave 的 SAVE 分支。
        app.Command("pt.dbase.save", "PT_DBASE_SAVE", "PT_DBASE_SAVE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            model.Save();
            ctx.Messages.Info($"Saved dbase model: {model.FullName}");
        });
    }

    private static void RegisterPtDbaseErase(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlErase 的模型级动作切片——retrieve 后从会话内存擦除。
        app.Command("pt.dbase.erase", "PT_DBASE_ERASE", "PT_DBASE_ERASE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var label = $"{model.FullName}";
            var erased = model.Erase();
            ctx.Messages.Info($"Erased dbase model: {label} erased={erased}");
        });
    }

    private static void RegisterPtDbaseCopy(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlnameCopy 的模型复制切片——复制为会话内存副本,
        // 用 CreoModel.Erase() 自清理副本(演示 .NET 封装的可组合性,无磁盘副作用)。
        app.Command("pt.dbase.copy", "PT_DBASE_COPY", "PT_DBASE_COPY_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var copy = model.CopyTo("CTKDBCOPY");
            if (copy is null)
                throw new InvalidOperationException($"Copy of {model.Name} failed.");
            try
            {
                ctx.Messages.Info($"Copied dbase model: {model.FullName} -> {copy.FullName}");
            }
            finally
            {
                copy.Erase();   // 清理会话内存副本,避免残留
            }
        });
    }

    private static void RegisterPtDbaseOrigin(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlOriginGet 的模型磁盘来源切片(只读 DATA)。
        app.Command("pt.dbase.origin", "PT_DBASE_ORIGIN", "PT_DBASE_ORIGIN_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var origin = model.GetOrigin();
            ctx.Messages.Info($"Model {model.FullName} origin: {origin ?? "(none)"}");
        });
    }

    private static void RegisterPtDbaseCommonName(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlCommonnameGet 的模型 common name 切片(只读;
        // native 侧 Creo 分配串经 ProWstringFree 释放,体现 L1 内存所有权铁律)。
        app.Command("pt.dbase.common-name", "PT_DBASE_COMMON_NAME", "PT_DBASE_COMMON_NAME_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var common = model.GetCommonName();
            ctx.Messages.Info($"Model {model.FullName} common name: {common ?? "(none)"}");
        });
    }

    private static void RegisterPtDbaseDependencies(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlDependenciesDataList 的依赖列表切片
        // (B 类 ProArray-of-struct DATA,首次模式;native 经 ProArrayFree 释放 Creo 内存)。
        app.Command("pt.dbase.dependencies", "PT_DBASE_DEPENDENCIES", "PT_DBASE_DEPENDENCIES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var deps = model.ListDependencies();
            if (deps.Count == 0)
                ctx.Messages.Info($"Model {model.FullName} has no dependencies.");
            else
                ctx.Messages.Info($"Model {model.FullName} depends on {deps.Count} item(s): {string.Join(", ", deps)}");
        });
    }

    private static void RegisterPtDbaseDisplay(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // pt_examples/pt_dbase/TestDbms.c: ProMdlDisplay 的实体动作切片。Display 是实体动作,
        // 落 CreoSolid(2D Drawing 不暴露此入口),体现类型层级让"仅实体有效"的操作编译期挡 2D。
        app.Command("pt.dbase.display", "PT_DBASE_DISPLAY", "PT_DBASE_DISPLAY_HELP", ctx =>
        {
            // modelType 是 Part → 必为 CreoSolid;Display 仅 Solid 暴露。
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var solid = (CreoSolid)model;
            solid.Display();
            ctx.Messages.Info($"Displayed model: {solid.FullName}");
        });
    }

    private static void RegisterPtDbaseOutline(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.dbase.outline", "PT_DBASE_OUTLINE", "PT_DBASE_OUTLINE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var solid = (CreoSolid)model;
            var box = solid.GetOutline();
            ctx.Messages.Info($"Outline of {solid.FullName}: " +
                $"min=({box.MinX:F3},{box.MinY:F3},{box.MinZ:F3}) " +
                $"max=({box.MaxX:F3},{box.MaxY:F3},{box.MaxZ:F3})");
        });
    }

    private static void RegisterPtDbaseMassProps(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.dbase.mass-props", "PT_DBASE_MASS_PROPS", "PT_DBASE_MASS_PROPS_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var solid = (CreoSolid)model;
            var mp = solid.GetMassProperty();
            ctx.Messages.Info($"Mass properties of {solid.FullName}: " +
                $"volume={mp.Volume:F3} area={mp.SurfaceArea:F3} density={mp.Density:F6} mass={mp.Mass:F3} " +
                $"cog=({mp.CenterOfGravityX:F3},{mp.CenterOfGravityY:F3},{mp.CenterOfGravityZ:F3})");
        });

    }

    private static void RegisterPtDbaseConfigOptions(CreoAppBuilder app)
    {
        // ProConfigoptArrayGet 多值配置项直绑 (ProArray-of-wstring DATA)。
        // 覆盖 CreoSession.ListConfigOptionValues → CreoNativeBridge.ConfigOptionArrayGet 读回真机点位。
        app.Command("pt.dbase.config-options", "PT_DBASE_CONFIG_OPTIONS", "PT_DBASE_CONFIG_OPTIONS_HELP", ctx =>
        {
            var session = ctx.Session
                ?? throw new InvalidOperationException("No Creo session for config-options probe.");
            const string option = "search_path";
            var values = session.ListConfigOptionValues(option);
            var preview = values.Count > 0 ? values[0] : "(unset)";
            ctx.Messages.Info($"config-option values={values.Count} option={option} first={preview}");
        });
    }
}
