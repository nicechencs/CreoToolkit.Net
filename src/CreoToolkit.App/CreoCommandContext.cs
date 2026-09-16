using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// 命令回调上下文：业务经 <see cref="Session"/> 读模型、经 <see cref="Messages"/> 弹消息。
/// <see cref="Session"/> 可空——脱 Creo 路由测试可不构造真实会话（F10）。
/// </summary>
public sealed class CreoCommandContext
{
    /// <summary>当前 Creo 会话（脱 Creo 测试可为 null）。</summary>
    public CreoSession? Session { get; }

    /// <summary>消息门面。</summary>
    public CreoMessages Messages { get; }

    internal CreoCommandContext(CreoSession? session, CreoMessages messages)
    {
        Session = session;
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
    }
}
