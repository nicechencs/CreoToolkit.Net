using CreoToolkit.App;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;
using CreoToolkit.Sdk.Units;

namespace CreoToolkit.Samples.PtParamsUnits;

/// <summary>
/// 参数单位端到端 sample:read/write/convert + 模型单位系统读演示。
/// <list type="bullet">
/// <item>pt.units.roundtrip — 取 mm/inch length 单位,SetWithUnits(parameterName, value, mm),
/// GetWithUnits 重读 + ConvertTo(mm→inch) 算 scale 校验线性算式。</item>
/// <item>pt.units.principal-system — 取主单位系统 + 列出所有单位系统名,
/// 验证 GetPrincipalUnitSystem / UnitSystems() 在真 Creo 下的行为。</item>
/// </list>
/// </summary>
public static class PtParamsUnitsRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        ThrowUtil.IfNullOrWhiteSpace(parameterName);
        RegisterUnitsRoundTrip(app, modelName, modelType, parameterName);
        RegisterPrincipalSystem(app, modelName, modelType);
        RegisterSetPrincipalSystem(app, modelName, modelType);
        RegisterAssignCrossQuantity(app, modelName, modelType, parameterName);
    }

    private static void RegisterUnitsRoundTrip(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        app.Command("pt.units.roundtrip", "PT_UNITS_ROUNDTRIP", "PT_UNITS_ROUNDTRIP_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            // 取 length 单位 mm + in(Creo 单位名为 "in" 非 "inch";模型必须包含,否则跳过)
            var mm = model.GetUnit("mm");
            var inch = model.GetUnit("in");
            if (mm is null || inch is null)
            {
                ctx.Messages.Info($"units roundtrip skipped: mm={mm.HasValue} inch={inch.HasValue} on {model.FullName}");
                return;
            }

            try
            {
                // round-trip 写 + 读
                const double valueMm = 25.4;
                model.Parameters.SetWithUnits(parameterName, valueMm, mm);
                var read = model.Parameters.GetWithUnits(parameterName);

                // 算 mm→inch 换算系数(应 ≈ 0.03937,offset=0)
                var conv = mm.Value.ConvertTo(inch.Value, ctx.Session!);
                var converted = conv.Apply(valueMm);

                ctx.Messages.Info(
                    $"units roundtrip OK: param={parameterName} write={valueMm}mm read={read?.Value:F4}" +
                    $"{read?.Unit?.Name ?? "?"} mm→inch scale={conv.Scale:F5} offset={conv.Offset:F2} " +
                    $"converted={converted:F4}");
            }
            catch (CreoException ex)
            {
                ctx.Messages.Info($"units roundtrip failed: param={parameterName} on {model.FullName} rc={ex.ErrorCode} {ex.Message}");
            }
        });
    }

    /// <summary>跨 quantity AssignUnit 守卫验证:建 length 参数(mm) → 尝试 assign mass 单位 → 预期 UnitTypeMismatchException。</summary>
    private static void RegisterAssignCrossQuantity(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        app.Command("pt.units.assign-cross-quantity", "PT_UNITS_ASSIGN_CROSS_QUANTITY", "PT_UNITS_ASSIGN_CROSS_QUANTITY_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            // 取 length 单位 mm 和 mass 单位 kg(两个不同 quantity)
            var mm = model.GetUnit("mm");
            var kg = model.GetUnit("kg");
            if (mm is null || kg is null)
            {
                ctx.Messages.Info($"assign-cross-quantity skipped: mm={mm.HasValue} kg={kg.HasValue} on {model.FullName}");
                return;
            }

            var testParam = $"{parameterName}_XQTY";
            try
            {
                // 建带 mm 单位的参数
                model.Parameters.SetWithUnits(testParam, 100.0, mm);

                // 尝试 AssignUnit(kg) — 应抛 UnitTypeMismatchException
                try
                {
                    model.Parameters.AssignUnit(testParam, kg.Value);
                    ctx.Messages.Info($"assign-cross-quantity failed: no exception thrown on {model.FullName}");
                }
                catch (UnitTypeMismatchException ex)
                {
                    ctx.Messages.Info($"assign-cross-quantity OK: caught {ex.GetType().Name} — {ex.Message}");
                }
            }
            catch (CreoException ex)
            {
                ctx.Messages.Info($"assign-cross-quantity failed: param={testParam} on {model.FullName} rc={ex.ErrorCode} {ex.Message}");
            }
            finally
            {
                // 清理测试参数
                try { model.Parameters.Delete(testParam); } catch { /* 清理失败不阻断 */ }
            }
        });
    }

    /// <summary>SetPrincipalUnitSystem 验证。
    /// sample 默认走"skipped: 缺 CTK_REAL_CREO_ALLOW_U3=1 显式启用"路径。</summary>
    private static void RegisterSetPrincipalSystem(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.units.set-principal-system", "PT_UNITS_SET_PRINCIPAL_SYSTEM", "PT_UNITS_SET_PRINCIPAL_SYSTEM_HELP", ctx =>
        {
            if (Environment.GetEnvironmentVariable("CTK_REAL_CREO_ALLOW_U3") != "1")
            {
                ctx.Messages.Info("set-principal-system skipped: CTK_REAL_CREO_ALLOW_U3 未启用");
                return;
            }
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            if (model is not CreoSolid solid)
            {
                ctx.Messages.Info($"set-principal-system skipped: model={model.FullName} 不是 solid(收紧 CreoSolid)");
                return;
            }

            var before = solid.GetPrincipalUnitSystem();
            var systems = solid.ListUnitSystems();
            // 挑一个与当前主系统同 type 但不同名的 target
            var target = systems.FirstOrDefault(s => s.Type == before?.Type && s.Name != before?.Name);
            if (target.IsUninitialized)
            {
                ctx.Messages.Info($"set-principal-system skipped: 无 type={before?.Type} 且非当前主系统的候选(现有 {systems.Count} 个)");
                return;
            }

            try
            {
                solid.SetPrincipalUnitSystem(target, CreoUnitConversionMode.PreservePhysicalSize);
                var after = solid.GetPrincipalUnitSystem();
                ctx.Messages.Info(
                    $"set-principal-system OK: model={solid.FullName} " +
                    $"before={before?.Name} after={after?.Name} target={target.Name} " +
                    $"mode=PreservePhysicalSize");
            }
            catch (CreoException ex)
            {
                ctx.Messages.Info($"set-principal-system failed: rc={ex.ErrorCode} {ex.Message}");
            }
            catch (UnitSystemTypeMismatchException ex)
            {
                ctx.Messages.Info($"set-principal-system failed: type-mismatch {ex.Message}");
            }
        });
    }

    private static void RegisterPrincipalSystem(CreoAppBuilder app, string modelName, CreoModelType modelType)
    {
        app.Command("pt.units.principal-system", "PT_UNITS_PRINCIPAL_SYSTEM", "PT_UNITS_PRINCIPAL_SYSTEM_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;

            var principal = model.GetPrincipalUnitSystem();
            var systems = model.ListUnitSystems();

            if (principal is null && systems.Count == 0)
            {
                ctx.Messages.Info($"principal-system skipped: model={model.FullName} has no unit systems defined (可能非 solid 模型)");
                return;
            }

            var systemNames = string.Join(", ", systems.Select(s => $"{s.Name}({s.Type})"));
            ctx.Messages.Info(
                $"principal-system OK: model={model.FullName} " +
                $"principal={principal?.Name ?? "(none)"}({principal?.Type.ToString() ?? "-"}) " +
                $"systems[{systems.Count}]={systemNames}");
        });
    }
}
