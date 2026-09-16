using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;

namespace CreoToolkit.Agent.Verbs;

internal static class SessionInfoHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(CreoSession session, AgentCommand command)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);

        return new Dictionary<string, object?>
        {
            ["attached"] = true,
        };
    }
}
