namespace CreoToolkit.Agent.Commands;

/// <summary>Agent 路由结果 DTO:成功带 Data,失败带 Error/Reason。</summary>
public sealed record AgentResult
{
    public bool Ok { get; }
    public IReadOnlyDictionary<string, object?>? Data { get; }
    public string? Error { get; }
    public string? Reason { get; }
    public string CorrelationId { get; }

    private AgentResult(
        bool ok,
        IReadOnlyDictionary<string, object?>? data,
        string? error,
        string? reason,
        string correlationId)
    {
        ThrowUtil.IfNullOrWhiteSpace(correlationId);
        Ok = ok;
        Data = data;
        Error = error;
        Reason = reason;
        CorrelationId = correlationId;
    }

    public static AgentResult Success(string correlationId, IReadOnlyDictionary<string, object?> data)
    {
        ThrowUtil.IfNull(data);
        return new AgentResult(true, data, null, null, correlationId);
    }

    public static AgentResult Fail(string correlationId, string error, string reason)
    {
        ThrowUtil.IfNullOrWhiteSpace(error);
        ThrowUtil.IfNullOrWhiteSpace(reason);
        return new AgentResult(false, null, error, reason, correlationId);
    }
}
