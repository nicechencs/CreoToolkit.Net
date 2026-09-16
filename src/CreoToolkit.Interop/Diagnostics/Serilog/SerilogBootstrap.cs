using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using global::Serilog;
using global::Serilog.Core;
using global::Serilog.Events;
using global::Serilog.Formatting.Display;
using CreoToolkit.Interop.Diagnostics;

namespace CreoToolkit.Interop.Diagnostics.Serilog;

/// <summary>Serilog 引擎构建参数。FilePath 必须已含 -yyyyMMdd-HHmmss-pid 后缀（由 BuildLogFilePath 生成）。
/// CreoLogLevel / CreoLogFormat 复用 CreoLog.cs 的同名枚举（命名空间 CreoToolkit.Interop.Diagnostics）。</summary>
public sealed record SerilogLogOptions(
    string FilePath,
    CreoToolkit.Interop.Diagnostics.CreoLogLevel Level,
    CreoToolkit.Interop.Diagnostics.CreoLogFormat Format,
    int RetentionDays,
    string Layer,
    LoggingLevelSwitch? LevelSwitch = null);

/// <summary>
/// Serilog 引擎引导：把 CreoLog 的语义参数翻译成 LoggerConfiguration，
/// 挂 File sink（JsonlFormatter）+ 可选 stderr Console sink（纯文本）。
/// 文件名滚动不开启（由 FilePath 唯一性保证）。
/// </summary>
public static class SerilogBootstrap
{
    // 形如 -20260626-231542-13580.log 的尾部模式
    private static readonly Regex StampedPattern =
        new(@"-\d{8}-\d{6}-\d+\.[A-Za-z0-9]+$", RegexOptions.Compiled);

    // 单文件上限：超过后 Serilog 自动滚动为 <stem>_001.log 等，避免长驻会话无界增长。
    // 保留策略不交给 Serilog（retainedFileCountLimit=null），统一由 CleanupOldLogs 按 mtime 清理。
    private const long DefaultFileSizeLimitBytes = 32L * 1024 * 1024;

    private const string TextFileTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}][{Level:u3}][{Layer}][{Loc}] {Message}{NewLine}{Exception}";

    private const string ConsoleTemplate =
        "[{Timestamp:HH:mm:ss}][{Level:u3}][{Layer}][{Loc}] {Message}{NewLine}{Exception}";

    /// <summary>
    /// 根据 options 构建 Serilog ILogger。
    /// 文件格式遵循 <see cref="CreoLogFormat"/>：text=文本文件；json/both=JSONL 文件。
    /// both 额外挂 stderr Console sink 输出纯文本；json 不输出 console。
    /// LevelSwitch 非 null 时用动态级别开关，否则用构建期快照。
    /// </summary>
    public static global::Serilog.ILogger BuildLogger(SerilogLogOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));

        var cfg = options.LevelSwitch is not null
            ? new LoggerConfiguration().MinimumLevel.ControlledBy(options.LevelSwitch)
            : new LoggerConfiguration().MinimumLevel.Is(MapLevel(options.Level));

        // 文件 sink：text 走人类可读文本，json/both 走 JSONL（机器可读真源）。
        if (options.Format == CreoLogFormat.Text)
        {
            cfg = cfg.WriteTo.File(
                formatter: new MessageTemplateTextFormatter(TextFileTemplate, CultureInfo.InvariantCulture),
                path: options.FilePath,
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: DefaultFileSizeLimitBytes,
                retainedFileCountLimit: null,
                shared: false);
        }
        else
        {
            cfg = cfg.WriteTo.File(
                formatter: new JsonlFormatter(options.Layer),
                path: options.FilePath,
                rollingInterval: RollingInterval.Infinite,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: DefaultFileSizeLimitBytes,
                retainedFileCountLimit: null,
                shared: false);
        }

        // both：JSONL 文件 + stderr 文本（三态里唯一同时给人和机器输出的档位）。
        if (options.Format == CreoLogFormat.Both)
        {
            cfg = cfg.WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate: ConsoleTemplate);
        }

        return cfg.CreateLogger();
    }

    /// <summary>
    /// 启动时手动清理同 stem 过期日志。retentionDays&lt;=0 跳过。
    /// stemPrefix 例：creoapp（匹配 creoapp-*.log）。
    /// </summary>
    public static void CleanupOldLogs(string directory, string stemPrefix, int retentionDays)
    {
        if (retentionDays <= 0) return;
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(stemPrefix)) return;
        if (!Directory.Exists(directory)) return;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        // 同 stem 文件（含时间戳尾部）；扩展名通配
        var pattern = stemPrefix + "-*.*";
        string[] files;
        try
        {
            files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        foreach (var f in files)
        {
            try
            {
                var mtime = File.GetLastWriteTimeUtc(f);
                if (mtime < cutoff)
                {
                    File.Delete(f);
                }
            }
            catch
            {
                // 单文件失败不影响其他
            }
        }
    }

    /// <summary>2 参便捷 overload：从已加 stamp 的 filePath 自动推断 dir + stemPrefix。</summary>
    public static void CleanupOldLogs(string filePathHint, int retentionDays)
    {
        if (retentionDays <= 0) return;
        if (string.IsNullOrEmpty(filePathHint)) return;
        var dir = Path.GetDirectoryName(filePathHint);
        if (string.IsNullOrEmpty(dir)) dir = ".";
        var stem = Path.GetFileNameWithoutExtension(filePathHint);
        if (string.IsNullOrEmpty(stem)) return;
        // 剥掉 -yyyyMMdd-HHmmss-pid 尾部得到 prefix
        var stripped = Regex.Replace(stem, @"-\d{8}-\d{6}-\d+$", "");
        if (string.IsNullOrEmpty(stripped)) stripped = stem;
        CleanupOldLogs(dir, stripped, retentionDays);
    }

    /// <summary>
    /// 解析 baseHint 的 stem/ext，附加 -yyyyMMdd-HHmmss-pid 后缀。
    /// 若 baseHint 已匹配 -\d{8}-\d{6}-\d+\.ext 模式，原样返回（不二次追加）。
    /// </summary>
    public static string BuildLogFilePath(string baseHint)
    {
        if (string.IsNullOrEmpty(baseHint))
            throw new ArgumentException("baseHint 不能为空", nameof(baseHint));

        if (StampedPattern.IsMatch(baseHint))
            return baseHint;

        var dir = Path.GetDirectoryName(baseHint) ?? string.Empty;
        var ext = Path.GetExtension(baseHint);
        if (string.IsNullOrEmpty(ext)) ext = ".log";
        var stem = Path.GetFileNameWithoutExtension(baseHint);
        if (string.IsNullOrEmpty(stem)) stem = "creoapp";

        var now = DateTime.Now;
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        int pid;
        try { pid = Process.GetCurrentProcess().Id; }
        catch { pid = 0; }

        var fileName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}-{1}-{2}{3}",
            stem, stamp, pid, ext);

        return string.IsNullOrEmpty(dir) ? fileName : Path.Combine(dir, fileName);
    }

    private static LogEventLevel MapLevel(CreoLogLevel level) => level switch
    {
        CreoLogLevel.Trace => LogEventLevel.Verbose,
        CreoLogLevel.Info  => LogEventLevel.Information,
        CreoLogLevel.Warn  => LogEventLevel.Warning,
        CreoLogLevel.Error => LogEventLevel.Error,
        _ => LogEventLevel.Information,
    };
}
