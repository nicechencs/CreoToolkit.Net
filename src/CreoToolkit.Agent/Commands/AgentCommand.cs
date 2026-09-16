namespace CreoToolkit.Agent.Commands;

/// <summary>Agent 命令 DTO:动词 + 参数 + 关联 id。</summary>
public sealed record AgentCommand
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyArgs =
        new Dictionary<string, object?>(0);

    public string Verb { get; }
    public IReadOnlyDictionary<string, object?> Args { get; }
    public string CorrelationId { get; }

    public AgentCommand(string verb, IReadOnlyDictionary<string, object?> args, string correlationId)
    {
        ThrowUtil.IfNullOrWhiteSpace(verb);
        ThrowUtil.IfNull(args);
        ThrowUtil.IfNullOrWhiteSpace(correlationId);
        Verb = verb;
        Args = args;
        CorrelationId = correlationId;
    }

    public static AgentCommand Of(string verb)
        => new(verb, EmptyArgs, Guid.NewGuid().ToString("N"));

    public static AgentCommand OfWithArgs(string verb, params (string Key, object? Value)[] args)
    {
        ThrowUtil.IfNull(args);
        return new AgentCommand(
            verb,
            args.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal),
            Guid.NewGuid().ToString("N"));
    }

    public AgentCommand WithCorrelation(string correlationId)
        => new(Verb, Args, correlationId);
}
