namespace CreoToolkit.Agent.Policy;

/// <summary>策略决策:允许或拒绝,拒绝时带审计原因。</summary>
public sealed record PolicyDecision(bool Allow, string? DenyReason)
{
    public static readonly PolicyDecision Allowed = new(true, null);

    public static PolicyDecision Denied(string reason)
    {
        ThrowUtil.IfNullOrWhiteSpace(reason);
        return new PolicyDecision(false, reason);
    }
}
