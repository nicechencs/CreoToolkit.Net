namespace CreoToolkit.App.Errors;

/// <summary>
/// 命令桥 native rc != 0 抛出。CreoAppHost.Run() 严格校验 BridgeInitialize/CommandActionAdd/
/// CommandDesignate/MenubarPushbuttonAdd 的返回码,任一非零即抛——避免 native user_initialize
/// 错误地返回 0(silent skip)。抓到的既有漏洞修复点。
/// </summary>
public sealed class CreoBridgeException : InvalidOperationException
{
    public CreoBridgeException(string message) : base(message) { }
    public CreoBridgeException(string message, Exception inner) : base(message, inner) { }
}
