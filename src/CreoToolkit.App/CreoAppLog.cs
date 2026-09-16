using System.Runtime.CompilerServices;
using CreoToolkit.Interop.Diagnostics;

namespace CreoToolkit.App;

/// <summary>
/// 应用层公开日志门面：保留既有结构化字段，同时让应用/示例代码
/// 不依赖 Interop 诊断类型与后端选型。
/// </summary>
public static class CreoAppLog
{
    public static void Trace(string message, string? @event = null, string? module = null,
        object? props = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => CreoLog.Trace(message, @event, module, props, CreoLogLayer.App, file: file, line: line);

    public static void Info(string message, string? @event = null, string? module = null,
        object? props = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => CreoLog.Info(message, @event, module, props, CreoLogLayer.App, file: file, line: line);

    public static void Warn(string message, string? @event = null, string? module = null,
        object? props = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => CreoLog.Warn(message, @event, module, props, CreoLogLayer.App, file: file, line: line);

    public static void Error(string message, string? @event = null, string? module = null,
        object? props = null, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        => CreoLog.Error(message, @event, module, props, CreoLogLayer.App, file: file, line: line);

    public static void Error(string message, Exception exception, string? @event = null,
        string? module = null, object? props = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        ThrowUtil.IfNull(exception);
        CreoLog.Error(message, exception, @event, module, props, CreoLogLayer.App, file, line);
    }
}
