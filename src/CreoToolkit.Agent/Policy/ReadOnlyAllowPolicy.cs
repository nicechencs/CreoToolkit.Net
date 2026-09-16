using CreoToolkit.Agent.Commands;

namespace CreoToolkit.Agent.Policy;

/// <summary>默认策略:只读 verb 放行;写 verb 默认拒,需显式 allowlist。</summary>
public sealed class ReadOnlyAllowPolicy : IAgentPolicy
{
    private static readonly HashSet<string> ReadOnlyDefaults = new(StringComparer.Ordinal)
    {
        "session.info",
        "model.summary",
    };

    private readonly HashSet<string> _allowedWrites;

    public ReadOnlyAllowPolicy(IEnumerable<string>? allowedWriteVerbs = null)
        => _allowedWrites = new HashSet<string>(
            allowedWriteVerbs ?? Array.Empty<string>(),
            StringComparer.Ordinal);

    public PolicyDecision Evaluate(AgentCommand command)
    {
        ThrowUtil.IfNull(command);

        if (ReadOnlyDefaults.Contains(command.Verb))
            return PolicyDecision.Allowed;

        if (_allowedWrites.Contains(command.Verb))
            return PolicyDecision.Allowed;

        return PolicyDecision.Denied(
            $"verb '{command.Verb}' 不在默认只读集 / 白名单写集 — Agent 不可信默认拒。");
    }
}
