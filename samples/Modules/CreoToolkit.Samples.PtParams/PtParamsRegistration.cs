using CreoToolkit.App;
using CreoToolkit.Sdk;

namespace CreoToolkit.Samples.PtParams;

public static class PtParamsRegistration
{
    public static void Register(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        ThrowUtil.IfNull(app);
        ThrowUtil.IfNullOrWhiteSpace(modelName);
        ThrowUtil.IfNullOrWhiteSpace(parameterName);
        RegisterPtParamsList(app, modelName, modelType, parameterName);
        RegisterPtParamsPage(app, modelName, modelType, parameterName);
        RegisterPtParamsRoundTrip(app, modelName, modelType, parameterName);
        RegisterPtParamsDeleteRoundTrip(app, modelName, modelType, parameterName);
        RegisterPtParamsDesignationRoundTrip(app, modelName, modelType, parameterName);
        RegisterPtParamsResetRoundTrip(app, modelName, modelType, parameterName);
        RegisterPtParamsUpsertScan(app, modelName, modelType, parameterName);
        RegisterPtParamsFilterByPrefix(app, modelName, modelType, parameterName);
        RegisterPtParamsFilterByType(app, modelName, modelType, parameterName);
        }

    private static void RegisterPtParamsList(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterValueGet 列表切片(now via WithUnits 后继)。
        app.Command("pt.params.list", "PT_PARAMS_LIST", "PT_PARAMS_LIST_HELP", ctx =>
        {
            var model = ctx.Session?.Models.GetCurrent();
            if (model is null)
            {
                ctx.Messages.Info(CreoCommandContextExtensions.NoCurrentModel);
                return;
            }

            var parameters = model.Parameters.List();
            var preview = string.Join(", ", parameters.Take(3).Select(FormatParameterPreview));
            ctx.Messages.Info($"{model.FullName} parameters={parameters.Count}" +
                              (parameters.Count > 0 ? $"; {preview}" : ""));
        });
    }

    private static void RegisterPtParamsPage(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterValueGet 列表展示的大模型分页写法(now via WithUnits 后继)。
        app.Command("pt.params.page", "PT_PARAMS_PAGE", "PT_PARAMS_PAGE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var parameters = model.Parameters.ListPage(0, 5);
            var preview = string.Join(", ", parameters.Select(FormatParameterPreview));
            ctx.Messages.Info($"{model.FullName} parameter page[0,5]={parameters.Count}" +
                              (parameters.Count > 0 ? $"; {preview}" : ""));
        });
    }

    private static void RegisterPtParamsRoundTrip(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterCreate/ValueSet/ValueGet 的模型级切片(now via WithUnits 后继)。
        app.Command("pt.params.roundtrip", "PT_PARAMS_ROUNDTRIP", "PT_PARAMS_ROUNDTRIP_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var value = CreoParamValue.OfString($"dotnet-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
            model.Parameters.Set(parameterName, value);
            var found = model.Parameters.Find(parameterName);
            if (found?.Value != value)
                throw new InvalidOperationException($"Parameter {parameterName} round-trip failed.");

            ctx.Messages.Info($"Parameter {parameterName} round-trip OK on {model.FullName}");
        });
    }

    private static void RegisterPtParamsDeleteRoundTrip(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterDelete 的模型级自动化切片。
        app.Command("pt.params.delete-roundtrip", "PT_PARAMS_DELETE_ROUNDTRIP",
            "PT_PARAMS_DELETE_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                var name = $"{parameterName}_DELETE";
                var value = CreoParamValue.OfString($"delete-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
                model.Parameters.Set(name, value);
                if (model.Parameters.Find(name) is null)
                    throw new InvalidOperationException($"Parameter {name} was not created before delete.");

                var deleted = model.Parameters.Delete(name);
                if (!deleted || model.Parameters.Find(name) is not null)
                    throw new InvalidOperationException($"Parameter {name} delete round-trip failed.");

                ctx.Messages.Info($"Parameter {name} delete round-trip OK on {model.FullName}");
            });
    }

    private static void RegisterPtParamsDesignationRoundTrip(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterDesignationVerify/Add/Remove 的模型级自动化切片。
        app.Command("pt.params.designation-roundtrip", "PT_PARAMS_DESIGNATION_ROUNDTRIP",
            "PT_PARAMS_DESIGNATION_ROUNDTRIP_HELP", ctx =>
            {
                if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
                var name = $"{parameterName}_DESIGNATE";
                var value = CreoParamValue.OfString($"designate-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
                model.Parameters.Set(name, value);
                try
                {
                    if (model.Parameters.IsDesignated(name) is true)
                        throw new InvalidOperationException($"Parameter {name} unexpectedly started designated.");
                    if (!model.Parameters.SetDesignated(name, true) || model.Parameters.IsDesignated(name) is not true)
                        throw new InvalidOperationException($"Parameter {name} designation add failed.");
                    if (!model.Parameters.SetDesignated(name, false) || model.Parameters.IsDesignated(name) is true)
                        throw new InvalidOperationException($"Parameter {name} designation remove failed.");

                    ctx.Messages.Info($"Parameter {name} designation round-trip OK on {model.FullName}");
                }
                finally
                {
                    model.Parameters.Delete(name);
                }
            });
    }

    private static void RegisterPtParamsResetRoundTrip(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // pt_examples/pt_params/TestParams.c: ProParameterValueReset 的模型级自动化切片。
        app.Command("pt.params.reset-roundtrip", "PT_PARAMS_RESET_ROUNDTRIP",
            "PT_PARAMS_RESET_ROUNDTRIP_HELP", ctx =>
            {
                // Regenerate 在 CreoSolid;modelType 是 Part → 必为 CreoSolid。
                if (!ctx.TryRetrieveModel(modelName, modelType, out var retrievedModel)) return;
                var model = (CreoSolid)retrievedModel;
                var name = $"{parameterName}_RESET";
                var initial = CreoParamValue.OfString($"initial-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
                var changed = CreoParamValue.OfString($"changed-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
                model.Parameters.Set(name, initial);
                try
                {
                    model.Regenerate();
                    model.Parameters.Set(name, changed);
                    if (!model.Parameters.Reset(name))
                        throw new InvalidOperationException($"Parameter {name} reset returned false.");
                    var found = model.Parameters.Find(name);
                    if (found?.Value != initial)
                        throw new InvalidOperationException($"Parameter {name} reset round-trip failed.");

                    ctx.Messages.Info($"Parameter {name} reset round-trip OK on {model.FullName}");
                }
                finally
                {
                    model.Parameters.Delete(name);
                }
            });
    }

    /// <summary>
    /// 对应 UgParamFeatLabel.c L14-81 核心算法（去 Selection + Message UI）。
    /// 批量 upsert 四种类型参数（string/int/double/bool），再 List 全量并汇总。
    /// C 端: ProParameterInit → 存在则 ProParameterValueWithUnitsSet, 不存在则 ProParameterWithUnitsCreate。
    /// L3 端: Parameters.Set（upsert 语义等价）→ Parameters.List → 格式化汇总。
    /// </summary>
    private static void RegisterPtParamsUpsertScan(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // UgParamFeatLabel.c: ProParameterInit/Create/Set upsert 算法切片（含多类型）。
        app.Command("pt.params.upsert-scan", "PT_PARAMS_UPSERT_SCAN", "PT_PARAMS_UPSERT_SCAN_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var prefix = $"{parameterName}_SCAN";

            // 对应 UgParamFeatLabel.c L58-78: 四种类型参数 upsert（各一次 Set=ProParameterCreate-or-Update, now via WithUnits 后继）。
            model.Parameters.Set($"{prefix}_STR",  CreoParamValue.OfString("label-dotnet"));
            model.Parameters.Set($"{prefix}_INT",  CreoParamValue.OfInt(42));
            model.Parameters.Set($"{prefix}_DBL",  CreoParamValue.OfDouble(3.14));
            model.Parameters.Set($"{prefix}_BOOL", CreoParamValue.OfBool(true));

            // 对应 TestParams.c ProTestShowParamsList L399-461: 全量 List + 格式化每项。
            var all = model.Parameters.List();
            var lines = all.Select(FormatParamRow).ToList();

            ctx.Messages.Info(
                $"upsert-scan OK on {model.FullName}: " +
                $"total={all.Count}; " +
                string.Join("; ", lines.Take(8)));
        });
    }

    /// <summary>
    /// 对应 TestParams.c L319-345 ProParameterSelect 按名前缀过滤集合（NamePrefix query）。
    /// C 端 ProParameterSelect filter_string = "CTK_SAMPLE_PARAM_SCAN_*" 的等价 .NET 实现。
    /// </summary>
    private static void RegisterPtParamsFilterByPrefix(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // TestParams.c L328-345: ProParameterSelect context+filter → ProTestShowParamsList 切片。
        app.Command("pt.params.filter-prefix", "PT_PARAMS_FILTER_PREFIX", "PT_PARAMS_FILTER_PREFIX_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var prefix = $"{parameterName}_SCAN";
            var query = new ParameterQuery(NamePrefix: prefix);
            var filtered = model.Parameters.List(query);

            ctx.Messages.Info(
                $"filter-prefix '{prefix}' on {model.FullName}: count={filtered.Count}" +
                (filtered.Count > 0
                    ? "; " + string.Join(", ", filtered.Take(5).Select(FormatParamRow))
                    : ""));
        });
    }

    /// <summary>
    /// 对应 TestParams.c L222-238 ProTestGetParamType 菜单 + ProParameterSelect 按类型过滤。
    /// C 端交互式类型选择的等价 .NET 实现：扫描 Double 参数子集。
    /// </summary>
    private static void RegisterPtParamsFilterByType(CreoAppBuilder app, string modelName, CreoModelType modelType, string parameterName)
    {
        // TestParams.c L222-238 + L328: 按 PRO_PARAM_DOUBLE 过滤的 ProParameterSelect 等价切片。
        app.Command("pt.params.filter-type", "PT_PARAMS_FILTER_TYPE", "PT_PARAMS_FILTER_TYPE_HELP", ctx =>
        {
            if (!ctx.TryRetrieveModel(modelName, modelType, out var model)) return;
            var query = new ParameterQuery(Kind: CreoParamValueKind.Double);
            var doubles = model.Parameters.List(query);

            ctx.Messages.Info(
                $"filter-type Double on {model.FullName}: count={doubles.Count}" +
                (doubles.Count > 0
                    ? "; " + string.Join(", ", doubles.Take(5).Select(p => $"{p.Name}={p.Value.AsDouble():F4}"))
                    : ""));
        });

    }

    private static string FormatParameterPreview(CreoParameter parameter)
        => $"{parameter.Name}={parameter.Value}{(parameter.IsModified ? " modified" : "")}";

    /// <summary>
    /// 对应 TestParams.c ProTestShowParamsList L410-454: Name+Mod+Type+Value+Designated 行格式化。
    /// C 端:   ProParameterValueGet(now via WithUnits 后继) + ProParameterIsModified + ProParameterDesignationVerify。
    /// L3 端:  CreoParameter.Value.Kind / .AsXxx() / .IsModified + Parameters.IsDesignated()。
    /// 此处简化为单行格式（不含 designation，designation 在专属 roundtrip 命令测试）。
    /// </summary>
    private static string FormatParamRow(CreoParameter p) =>
        $"{p.Name}:{p.Value.Kind}={FormatValue(p.Value)}{(p.IsModified ? "[M]" : "")}";

    private static string FormatValue(CreoParamValue v) => v.Kind switch
    {
        CreoParamValueKind.String => v.AsString(),
        CreoParamValueKind.Double => v.AsDouble().ToString("F4"),
        CreoParamValueKind.Int    => v.AsInt().ToString(),
        CreoParamValueKind.Bool   => v.AsBool().ToString(),
        _                         => "?"
    };
}
