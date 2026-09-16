namespace CreoToolkit.App;

/// <summary>
/// 消息门面：业务只调 <see cref="Info"/>，看不到 native 边界。msgFile 与底层 display 接缝由 host 注入，
/// 便于脱 Creo 单测（fake display 记录 (msgFile,text)）。
/// </summary>
public sealed class CreoMessages
{
    private readonly string _msgFile;
    private readonly Action<string, string> _display;   // (msgFile, text)

    internal CreoMessages(string msgFile, Action<string, string> display)
    {
        _msgFile = msgFile ?? "";
        _display = display ?? throw new ArgumentNullException(nameof(display));
    }

    /// <summary>在消息区显示一条信息。</summary>
    public void Info(string text) => _display(_msgFile, text ?? "");
}
