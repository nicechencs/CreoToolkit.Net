using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CreoToolkit.Interop.Diagnostics;
using Xunit;

namespace CreoToolkit.Interop.Tests;

public class CreoLogBehaviorTests : IDisposable
{
    private readonly string _dir;

    public CreoLogBehaviorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ctk-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        CreoLog.CloseFile();
        CreoLog.SetSink(null);
        CreoLog.ResetOverrides();
        CreoLog.LoadFromEnvironment();
        try { Directory.Delete(_dir, true); } catch { /* 最多文件占用，忽略 */ }
    }

    [Fact]
    public void JsonFormat_WritesJsonl_And_AppliesLevelThreshold()
    {
        var path = Path.Combine(_dir, "a.log");
        CreoLog.ResetForTest(path, format: "json", level: "warn", ctkEnv: null);

        CreoLog.Error("e", @event: "t.e");
        CreoLog.Warn("w", @event: "t.w");
        CreoLog.Info("i", @event: "t.i");   // 阈值 warn：应被过滤

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Assert.Equal("error", doc.RootElement.GetProperty("level").GetString());
            Assert.Equal("t.e", doc.RootElement.GetProperty("event").GetString());
        }
        using (var doc = JsonDocument.Parse(lines[1]))
            Assert.Equal("warn", doc.RootElement.GetProperty("level").GetString());
    }

    [Fact]
    public void TextFormat_WritesPlainText_NotJson()
    {
        var path = Path.Combine(_dir, "b.log");
        CreoLog.ResetForTest(path, format: "text", level: "trace", ctkEnv: null);

        CreoLog.Info("hello world");

        var text = File.ReadAllText(path);
        Assert.Contains("hello world", text);
        Assert.DoesNotContain("\"level\"", text);
        Assert.DoesNotContain("\"props\"", text);
    }

    [Fact]
    public void BothFormat_WritesTextAndJsonLines()
    {
        var path = Path.Combine(_dir, "c.log");
        // Configure the production logger through the environment, then select the
        // literal test path. This keeps the file sink and stderr console sink real.
        using var logger = new RealLoggerScope(path, format: "both", level: "trace", captureError: true);

        CreoLog.Info("both-msg");
        CreoLog.CloseFile();

        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
        using (var doc = JsonDocument.Parse(lines[0]))
        {
            Assert.Equal("info", doc.RootElement.GetProperty("level").GetString());
            Assert.Equal("both-msg", doc.RootElement.GetProperty("msg").GetString());
        }

        var stderr = logger.CapturedError!.ToString();
        Assert.Contains("both-msg", stderr);
        Assert.DoesNotContain("\"level\"", stderr);
    }

    [Fact]
    public void RuntimeLevelChange_TakesEffect_OnRealFileLogger()
    {
        // ResetForTest 装 external sink 会绕过真实 Serilog 文件 logger；
        // 本用例走 SetFile + 真实 logger，验证 LoggingLevelSwitch 动态跟随。
        var path = Path.Combine(_dir, "d.log");
        using var logger = new RealLoggerScope(path, format: "json", level: "trace");

        CreoLog.Level = CreoLogLevel.Warn;

        CreoLog.Warn("w1");
        CreoLog.Trace("t-filtered");
        CreoLog.Level = CreoLogLevel.Trace;   // 降级：文件 logger 必须立即放行 trace
        CreoLog.Trace("t-visible");
        CreoLog.CloseFile();

        var content = File.ReadAllText(path);
        Assert.Contains("w1", content);
        Assert.DoesNotContain("t-filtered", content);
        Assert.Contains("t-visible", content);
    }

    [Fact]
    public void ProductionPath_Msg_IsNotQuoted()
    {
        // 回归：Serilog 4 MessageTemplate.Render 默认给字符串标量加引号，
        // 曾使 msg 变成 "\"hello-plain\""。
        var path = Path.Combine(_dir, "e.log");
        using var logger = new RealLoggerScope(path, format: "json", level: "trace");

        CreoLog.Info("hello-plain");
        CreoLog.CloseFile();

        using var doc = JsonDocument.Parse(File.ReadAllText(path).Trim());
        Assert.Equal("hello-plain", doc.RootElement.GetProperty("msg").GetString());
    }

    [Fact]
    public void JsonFormat_Msg_WithBraces_IsLiteral()
    {
        var path = Path.Combine(_dir, "f.log");
        CreoLog.ResetForTest(path, format: "json", level: "trace", ctkEnv: null);

        CreoLog.Info("value {level} literal");

        using var doc = JsonDocument.Parse(File.ReadAllLines(path)[0]);
        Assert.Equal("value {level} literal", doc.RootElement.GetProperty("msg").GetString());
    }

    private sealed class RealLoggerScope : IDisposable
    {
        private readonly string? _previousFormat;
        private readonly string? _previousLevel;
        private readonly string? _previousEnabled;
        private readonly TextWriter _previousError;
        private bool _disposed;

        public StringWriter? CapturedError { get; }

        public RealLoggerScope(string path, string format, string level, bool captureError = false)
        {
            _previousFormat = Environment.GetEnvironmentVariable("CTK_DOTNET_LOG_FORMAT");
            _previousLevel = Environment.GetEnvironmentVariable("CTK_DOTNET_LOG_LEVEL");
            _previousEnabled = Environment.GetEnvironmentVariable("CTK_DOTNET_LOG");
            _previousError = Console.Error;
            CapturedError = captureError
                ? new StringWriter(CultureInfo.InvariantCulture)
                : null;

            try
            {
                CreoLog.CloseFile();
                CreoLog.SetSink(null);

                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG_FORMAT", format);
                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG_LEVEL", level);
                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG", "on");
                if (CapturedError is not null)
                    Console.SetError(CapturedError);

                CreoLog.ResetOverrides();
                CreoLog.LoadFromEnvironment();
                CreoLog.SetFile(path, dailyRolling: false);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                CreoLog.CloseFile();
                CreoLog.SetSink(null);
                CreoLog.ResetOverrides();
            }
            finally
            {
                // Restore the process-global console first so a reload warning cannot
                // leak into a test's captured writer.
                Console.SetError(_previousError);
                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG_FORMAT", _previousFormat);
                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG_LEVEL", _previousLevel);
                Environment.SetEnvironmentVariable("CTK_DOTNET_LOG", _previousEnabled);
                CreoLog.LoadFromEnvironment();
                CapturedError?.Dispose();
            }
        }
    }
}
