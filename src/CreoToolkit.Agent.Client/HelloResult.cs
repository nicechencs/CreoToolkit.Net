namespace CreoToolkit.Agent.Client;

/// <summary>agent.hello 握手响应。</summary>
public sealed class HelloResult
{
    public int ProtocolVersion { get; }
    public IReadOnlyList<string> Verbs { get; }
    public IReadOnlyList<string> Capabilities { get; }

    public HelloResult(int protocolVersion, IReadOnlyList<string> verbs, IReadOnlyList<string> capabilities)
    {
        ProtocolVersion = protocolVersion;
        Verbs = verbs ?? throw new ArgumentNullException(nameof(verbs));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }
}
