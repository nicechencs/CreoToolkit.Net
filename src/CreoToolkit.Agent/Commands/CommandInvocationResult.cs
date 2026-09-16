namespace CreoToolkit.Agent.Commands;

/// <summary>命令派发状态。</summary>
public enum CommandInvocationStatus
{
    /// <summary>派发并成功执行。</summary>
    Ok = 0,
    /// <summary>宿主未找到该命令 id(未注册)。</summary>
    NotFound = 1,
    /// <summary>命令 handler 抛异常或返回失败。</summary>
    Failed = 2,
}

/// <summary>命令派发结果 DTO:Status 三态 + 可选诊断消息。</summary>
public sealed record CommandInvocationResult
{
    public CommandInvocationStatus Status { get; }
    /// <summary>诊断消息(NotFound 时携命令 id,Failed 时携异常类型/消息)。</summary>
    public string? Message { get; }

    private CommandInvocationResult(CommandInvocationStatus status, string? message)
    {
        Status = status;
        Message = message;
    }

    public static CommandInvocationResult Ok(string? message = null)
        => new(CommandInvocationStatus.Ok, message);

    public static CommandInvocationResult NotFound(string commandId)
    {
        ThrowUtil.IfNullOrWhiteSpace(commandId);
        return new(CommandInvocationStatus.NotFound, $"命令 '{commandId}' 未注册");
    }

    public static CommandInvocationResult Failed(string message)
    {
        ThrowUtil.IfNullOrWhiteSpace(message);
        return new(CommandInvocationStatus.Failed, message);
    }
}
