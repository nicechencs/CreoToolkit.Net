namespace CreoToolkit.Sdk.Errors;

/// <summary>
/// 本 SDK 所有领域异常的公共基类。调用方可用一个 catch 承接全部 SDK 异常，
/// 与框架异常（ArgumentException 等）区分开。错误语义靠具体类型的
/// <c>ErrorCode</c> 等字段区分，不走派生类型爆炸。
/// </summary>
public abstract class CreoToolkitException : Exception
{
    protected CreoToolkitException(string message)
        : base(message)
    {
    }

    protected CreoToolkitException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
