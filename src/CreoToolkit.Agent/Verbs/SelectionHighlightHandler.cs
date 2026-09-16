using CreoToolkit.Agent.Commands;
using CreoToolkit.Agent.Handles;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Verbs;

internal static class SelectionHighlightHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(
        CreoSession session,
        AgentCommand command,
        HandleRegistry registry)
    {
        ThrowUtil.IfNull(registry);

        var token = AgentArgs.GetHandleId(command, "token", required: true)!.Value;
        var index = AgentArgs.GetInt(command, "index");
        if (index < 0)
            throw new CreoException("Agent.SelectionHighlight", ProError.BadInputs, $"index 不能为负: {index}");

        var set = registry.Resolve<CreoSelectionSet>(token, expectedKind: "Selection")
            ?? throw new CreoException("Agent.SelectionHighlight", ProError.NotFound, "token 无效或已释放");

        if (index >= set.Count)
            throw new CreoException("Agent.SelectionHighlight", ProError.BadInputs, $"index {index} 越界(count={set.Count})");

        set[index].Highlight();

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["kind"] = "Selection",
            ["index"] = index,
        };
    }
}