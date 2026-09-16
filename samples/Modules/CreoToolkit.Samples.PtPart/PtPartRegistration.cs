using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtPart;

/// <summary>
/// TestPartMaterial.c 移植 (pt_examples/pt_part — 纯 read,无改盘)。
/// 3 命令包装 SDK 已直绑的 CreoPart 材料 API,不引入新 native code。
/// </summary>
public static class PtPartRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        RegisterPtMaterialListNames(app, modelName, modelType);
        RegisterPtMaterialCurrent(app, modelName, modelType);
        RegisterPtMaterialDensity(app, modelName, modelType);
    }

    private static void RegisterPtMaterialListNames(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestPartMaterial.c ProUtilSelectMaterial: ProPartMaterialsGet 列名切片(去 UI 菜单 → 纯枚举展示)。
        app.Command("pt.material.list-names", "PT_MATERIAL_LIST_NAMES", "PT_MATERIAL_LIST_NAMES_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var part = (CreoPart)model;
            var names = part.ListMaterialNames();
            var preview = string.Join(", ", names.Take(5));
            ctx.Messages.Info($"{model.FullName} materials={names.Count}" +
                              (names.Count > 0 ? $"; {preview}" : ""));
        });
    }

    private static void RegisterPtMaterialCurrent(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestPartMaterial.c ProTestMaterialAction CURRENT_MAT_QUERY: ProMaterialCurrentGet 切片。
        app.Command("pt.material.current", "PT_MATERIAL_CURRENT", "PT_MATERIAL_CURRENT_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var part = (CreoPart)model;
            var current = part.GetCurrentMaterial();
            ctx.Messages.Info(current is null
                ? $"{model.FullName} current material=(none)"
                : $"{model.FullName} current material={current}");
        });
    }

    private static void RegisterPtMaterialDensity(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        // TestPartMaterial.c ProTestMaterialAction MAT_PROP_QUERY:
        // ProMaterialPropertyGet(PRO_MATPROP_MASS_DENSITY) 切片(读 part-level 当前密度)。
        app.Command("pt.material.density", "PT_MATERIAL_DENSITY", "PT_MATERIAL_DENSITY_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var part = (CreoPart)model;
            var density = part.GetDensity();
            ctx.Messages.Info(density is null
                ? $"{model.FullName} density=(unsupported)"
                : $"{model.FullName} density={density.Value:F6}");
        });

    }
}
