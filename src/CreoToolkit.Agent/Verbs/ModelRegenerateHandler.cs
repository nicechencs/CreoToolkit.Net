using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Verbs;

internal static class ModelRegenerateHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(CreoSession session, AgentCommand command)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);

        var model = session.Models.GetCurrent();
        if (model is null)
            throw new CreoException("Agent.ModelRegenerate", ProError.NotFound, "无当前模型,无法 regenerate");

        var solid = model.AsSolid();
        if (solid is null)
            throw new CreoException(
                "Agent.ModelRegenerate",
                ProError.BadInputs,
                $"模型 '{model.Name}' 是 {model.Type},非 Part/Assembly,不支持 regenerate");

        solid.Regenerate();

        return new Dictionary<string, object?>
        {
            ["regenerated"] = true,
            ["name"] = model.Name,
            ["type"] = model.Type.ToString(),
        };
    }
}
