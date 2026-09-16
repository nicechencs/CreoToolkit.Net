using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CreoToolkit.Interop.Diagnostics.Serilog;
using global::Serilog.Events;
using global::Serilog.Parsing;
using Xunit;

// 日志门面是进程级静态状态 + 依赖环境/文件，禁用并行避免互扰。
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CreoToolkit.Interop.Tests;

public class JsonlFormatterTests
{
    [Fact]
    public void FieldOrder_And_Chinese_NotEscaped()
    {
        var ev = MakeEvent(LogEventLevel.Information, "中文消息 abc",
            new LogEventProperty("layer", new ScalarValue("L3")),
            new LogEventProperty("module", new ScalarValue("model")),
            new LogEventProperty("event", new ScalarValue("model.load.ok")),
            new LogEventProperty("tid", new ScalarValue(42)));

        var line = Format(ev, "L3");

        Assert.StartsWith("{\"ts\":", line);
        Assert.True(IndexOf(line, "\"ts\":") < IndexOf(line, "\"level\":"));
        Assert.True(IndexOf(line, "\"level\":") < IndexOf(line, "\"layer\":"));
        Assert.True(IndexOf(line, "\"layer\":") < IndexOf(line, "\"module\":"));
        Assert.True(IndexOf(line, "\"msg\":") < IndexOf(line, "\"props\":"));
        Assert.Equal(-1, IndexOf(line, "\\u"));                 // 中文不转义
        Assert.Contains("\"msg\":\"中文消息 abc\"", line);
        Assert.Contains("\"event\":\"model.load.ok\"", line);
        Assert.Contains("\"tid\":42", line);
        Assert.EndsWith("\n", line);
    }

    [Fact]
    public void Ts_Is_Utc_Millisecond_Z()
    {
        var ev = MakeEvent(LogEventLevel.Information, "x");
        using var doc = JsonDocument.Parse(Format(ev, "app"));
        var ts = doc.RootElement.GetProperty("ts").GetString();
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", ts);
    }

    [Fact]
    public void Module_Absent_Defaults_sdk_ExplicitNull_IsJsonNull()
    {
        using (var doc = JsonDocument.Parse(Format(MakeEvent(LogEventLevel.Information, "x"), "app")))
            Assert.Equal("sdk", doc.RootElement.GetProperty("module").GetString());

        var evNull = MakeEvent(LogEventLevel.Information, "x",
            new LogEventProperty("module", new ScalarValue(null)));
        using var docNull = JsonDocument.Parse(Format(evNull, "app"));
        Assert.Equal(JsonValueKind.Null, docNull.RootElement.GetProperty("module").ValueKind);
    }

    [Fact]
    public void Exception_Includes_Code_And_Inner_Chain()
    {
        var inner = new InvalidOperationException("inner");
        var outer = new IOException("outer", inner);
        var ev = new LogEvent(
            DateTimeOffset.UtcNow, LogEventLevel.Error, outer,
            new MessageTemplateParser().Parse("{Message}"),
            new[] { new LogEventProperty("Message", new ScalarValue("boom")) });

        using var doc = JsonDocument.Parse(Format(ev, "app"));
        var err = doc.RootElement.GetProperty("err");
        Assert.Equal(outer.HResult, err.GetProperty("code").GetInt32());
        Assert.Equal("System.IO.IOException", err.GetProperty("type").GetString());
        Assert.Equal("outer", err.GetProperty("msg").GetString());
        Assert.Equal("System.InvalidOperationException", err.GetProperty("inner").GetProperty("type").GetString());
    }

    [Fact]
    public void MessageTemplate_PreservesNumericFormatAndAlignment()
    {
        var ev = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("id={Id:x8}, name={Name,6}"),
            new[]
            {
                new LogEventProperty("Id", new ScalarValue(42)),
                new LogEventProperty("Name", new ScalarValue("Ada")),
            });

        Assert.Equal("id=0000002a, name=   Ada", JsonlFormatter.RenderMessage(ev));
    }

    private static LogEvent MakeEvent(LogEventLevel level, string message, params LogEventProperty[] props)
    {
        var list = new List<LogEventProperty> { new("Message", new ScalarValue(message)) };
        list.AddRange(props);
        return new LogEvent(DateTimeOffset.UtcNow, level, null,
            new MessageTemplateParser().Parse("{Message}"), list);
    }

    private static string Format(LogEvent ev, string layer)
    {
        var f = new JsonlFormatter(layer);
        using var sw = new StringWriter();
        f.Format(ev, sw);
        return sw.ToString();
    }

    private static int IndexOf(string s, string v) => s.IndexOf(v, StringComparison.Ordinal);
}
