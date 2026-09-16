using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Handles;
using CreoToolkit.Sdk;

namespace CreoToolkit.Agent.Verbs;

internal static class SelectionReleaseHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(
        CreoSession session,
        AgentCommand command,
        HandleRegistry registry)
    {
        ThrowUtil.IfNull(registry);

        var token = AgentArgs.GetHandleId(command, "token", required: true)!.Value;
        var released = registry.Release(token, expectedKind: "Selection");

        return new Dictionary<string, object?>
        {
            ["ok"] = released,
            ["kind"] = "Selection",
        };
    }
}