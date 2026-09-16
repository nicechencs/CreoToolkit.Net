using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;

namespace CreoToolkit.Agent.Verbs;

internal static class ModelSummaryHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(CreoSession session, AgentCommand command)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);

        var model = session.Models.GetCurrent();
        if (model is null)
        {
            return new Dictionary<string, object?>
            {
                ["current"] = null,
            };
        }

        var hasExtension = model.Type != CreoModelType.Unknown;
        return new Dictionary<string, object?>
        {
            ["current"] = true,
            ["name"] = model.Name,
            ["type"] = model.Type.ToString(),
            ["extension"] = hasExtension ? model.Extension : null,
            ["fullName"] = hasExtension ? model.FullName : null,
            ["isModified"] = model.IsModified(),
        };
    }
}
