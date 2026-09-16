using CreoToolkit.Agent.Commands;
using CreoToolkit.Sdk;
using CreoToolkit.Sdk.Errors;

namespace CreoToolkit.Agent.Verbs;

internal static class ParameterSetHandler
{
    public static IReadOnlyDictionary<string, object?> Handle(CreoSession session, AgentCommand command)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(command);

        var name = AgentArgs.GetString(command, "name", required: true)!;
        var kind = AgentArgs.GetString(command, "kind", required: true)!;
        var creoVal = kind switch
        {
            // v2: value 必填，string 分支也不做空串兜底。
            "string" => CreoParamValue.OfString(AgentArgs.GetString(command, "value", required: true)!),
            "int" => CreoParamValue.OfInt(AgentArgs.GetInt(command, "value")),
            "double" => CreoParamValue.OfDouble(AgentArgs.GetDouble(command, "value")),
            "bool" => CreoParamValue.OfBool(AgentArgs.GetBool(command, "value")),
            _ => throw new CreoException("Agent.ParameterSet", ProError.BadInputs, $"unknown kind: {kind}"),
        };

        var model = session.Models.GetCurrent()
            ?? throw new CreoException("Agent.ParameterSet", ProError.NotFound, "no current model; cannot set parameter");

        model.Parameters.Set(name, creoVal);

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["name"] = name,
            ["kind"] = kind,
            ["modelType"] = model.Type.ToString(),
        };
    }
}