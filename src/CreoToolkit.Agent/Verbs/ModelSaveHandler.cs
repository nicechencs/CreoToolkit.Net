using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Verbs;

internal static class ModelSaveHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(CreoSession session, AgentCommand command)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);

        var model = session.Models.GetCurrent()
            ?? throw new CreoException("Agent.ModelSave", ProError.NotFound, "无当前模型,无法保存");

        // best-effort fast-fail。
        // - allowed == false 明确不允许 → 早拒,省一次 ProMdlSave 调用
        // - allowed == null 表示"不可查询"(model 类型不支持/Creo 状态异常)→ 不拒,让 Save 异常说话
        // 检查到 Save 之间仍有 race,最终以 Save() 异常为准
        var allowed = model.IsSaveAllowed();
        if (allowed == false)
            throw new CreoException("Agent.ModelSave", ProError.BadInputs, $"保存不被允许 (IsSaveAllowed=false, model='{model.Name}')");

        model.Save();

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["name"] = model.Name,
            ["modelType"] = model.Type.ToString(),
        };
    }
}
