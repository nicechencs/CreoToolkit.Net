// Serilog → JSONL 输出器：严格按契约字段顺序，禁止 Unicode 转义中文
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using global::Serilog.Events;
using global::Serilog.Formatting;

namespace CreoToolkit.Interop.Diagnostics.Serilog
{
    /// <summary>JSONL 文本格式器：把 Serilog LogEvent 序列化为契约约定的一行 JSON。</summary>
    public sealed class JsonlFormatter : ITextFormatter
    {
        private readonly string _layer;
        private readonly string _defaultModule;

        // 写 JSON 时禁止把中文 escape 成 \uXXXX
        private static readonly JsonWriterOptions s_writerOptions = new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false,
        };

        public JsonlFormatter(string layer, string? defaultModule = null)
        {
            if (string.IsNullOrEmpty(layer)) throw new ArgumentException("layer 必填", nameof(layer));
            _layer = layer;
            _defaultModule = string.IsNullOrEmpty(defaultModule) ? "sdk" : defaultModule!;
        }

        public void Format(LogEvent logEvent, TextWriter output)
        {
            if (logEvent is null) throw new ArgumentNullException(nameof(logEvent));
            if (output is null) throw new ArgumentNullException(nameof(output));

            // 用 Utf8JsonWriter 写到 MemoryStream，保证字段顺序固定
            using var ms = new MemoryStream(256);
            using (var w = new Utf8JsonWriter(ms, s_writerOptions))
            {
                w.WriteStartObject();

                // 1. ts —— ISO-8601 UTC，毫秒精度，带 Z
                w.WriteString("ts", logEvent.Timestamp.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture));

                // 2. level —— 小写英文
                w.WriteString("level", MapLevel(logEvent.Level));

                // 3. layer —— 优先使用 LogEvent.Properties["layer"]，否则用构造时 _layer
                w.WriteString("layer", TryGetScalarString(logEvent, "layer") ?? _layer);

                // 4. module —— 区分 present-but-null 与 absent
                WriteNullableScalarStringField(w, "module", logEvent, _defaultModule);

                // 5. event —— 同上
                WriteNullableScalarStringField(w, "event", logEvent, string.Empty);

                // 6. msg —— 模板渲染
                w.WriteString("msg", logEvent.MessageTemplate.Render(logEvent.Properties, System.Globalization.CultureInfo.InvariantCulture));

                // 7. loc
                w.WriteString("loc", TryGetScalarString(logEvent, "loc") ?? string.Empty);

                // 8. props —— 对象或 null
                w.WritePropertyName("props");
                WritePropsField(w, logEvent);

                // 9. corr —— 字符串或 null
                w.WritePropertyName("corr");
                var corr = TryGetScalarString(logEvent, "corr");
                if (corr is null) w.WriteNullValue(); else w.WriteStringValue(corr);

                // 10. tid —— int
                w.WriteNumber("tid", ResolveTid(logEvent));

                // 11. err —— 对象或 null
                w.WritePropertyName("err");
                WriteErrField(w, logEvent);

                w.WriteEndObject();
                w.Flush();
            }

            // 写到 TextWriter，末尾换行
            output.Write(Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length));
            output.Write('\n');
        }

        // ---- 辅助 ----

        private static string MapLevel(LogEventLevel lvl) => lvl switch
        {
            LogEventLevel.Verbose => "trace",
            LogEventLevel.Debug => "trace",
            LogEventLevel.Information => "info",
            LogEventLevel.Warning => "warn",
            LogEventLevel.Error => "error",
            LogEventLevel.Fatal => "error",
            _ => "info",
        };

        /// <summary>
        /// 写一个 nullable 字符串字段：
        /// - key 未出现在 properties 中 → 写 absentDefault（字符串）
        /// - key 出现但 ScalarValue.Value 为 null → 写 JSON null
        /// - key 出现且为字符串（含 ""）→ 写该字符串
        /// </summary>
        private static void WriteNullableScalarStringField(Utf8JsonWriter w, string key, LogEvent ev, string absentDefault)
        {
            if (!ev.Properties.TryGetValue(key, out var v))
            {
                w.WriteString(key, absentDefault);
                return;
            }
            if (v is ScalarValue sv)
            {
                if (sv.Value is null)
                {
                    w.WriteNull(key);
                    return;
                }
                w.WriteString(key, sv.Value.ToString());
                return;
            }
            // 非标量退化为渲染
            w.WriteString(key, v.ToString().Trim('"'));
        }

        /// <summary>取 ScalarValue 的字符串值（去掉 Serilog 加的两端引号）。</summary>
        private static string? TryGetScalarString(LogEvent ev, string key)
        {
            if (!ev.Properties.TryGetValue(key, out var v)) return null;
            if (v is ScalarValue sv)
            {
                return sv.Value?.ToString();
            }
            // 非标量场景退化为渲染
            return v.ToString().Trim('"');
        }

        private static int ResolveTid(LogEvent ev)
        {
            if (ev.Properties.TryGetValue("tid", out var v) && v is ScalarValue sv && sv.Value is not null)
            {
                if (sv.Value is int i) return i;
                if (int.TryParse(sv.Value.ToString(), out var parsed)) return parsed;
            }
            return Environment.CurrentManagedThreadId;
        }

        /// <summary>把 LogEvent.Properties["props"] 写成 JSON 对象；缺则 null。</summary>
        private static void WritePropsField(Utf8JsonWriter w, LogEvent ev)
        {
            if (!ev.Properties.TryGetValue("props", out var v))
            {
                w.WriteNullValue();
                return;
            }
            WriteSerilogValue(w, v, forcePropsObject: true);
        }

        /// <summary>err 字段：优先用 LogEvent.Exception；否则回落 Properties["err"] 的
        /// 结构化对象(ForContext("err", WithError(ex)) 路径);两者皆无写 null。</summary>
        private static void WriteErrField(Utf8JsonWriter w, LogEvent ev)
        {
            var ex = ev.Exception;
            if (ex is not null)
            {
                w.WriteStartObject();
                w.WriteString("type", ex.GetType().FullName ?? ex.GetType().Name);
                w.WriteString("msg", ex.Message ?? string.Empty);
                w.WriteString("stack", ex.StackTrace ?? string.Empty);
                w.WriteEndObject();
                return;
            }
            if (ev.Properties.TryGetValue("err", out var v) && v is StructureValue)
            {
                WriteSerilogValue(w, v);
                return;
            }
            w.WriteNullValue();
        }

        /// <summary>把任意 LogEventPropertyValue 递归写为 JSON。</summary>
        private static void WriteSerilogValue(Utf8JsonWriter w, LogEventPropertyValue value, bool forcePropsObject = false)
        {
            switch (value)
            {
                case ScalarValue sv:
                    if (forcePropsObject)
                    {
                        // props 必须是对象/null，不允许标量
                        w.WriteNullValue();
                        return;
                    }
                    WriteScalar(w, sv.Value);
                    return;

                case StructureValue stv:
                    w.WriteStartObject();
                    foreach (var p in stv.Properties)
                    {
                        w.WritePropertyName(p.Name);
                        WriteSerilogValue(w, p.Value);
                    }
                    w.WriteEndObject();
                    return;

                case DictionaryValue dv:
                    w.WriteStartObject();
                    foreach (var kv in dv.Elements)
                    {
                        var key = kv.Key.Value?.ToString() ?? string.Empty;
                        w.WritePropertyName(key);
                        WriteSerilogValue(w, kv.Value);
                    }
                    w.WriteEndObject();
                    return;

                case SequenceValue seq:
                    if (forcePropsObject)
                    {
                        // spec: props 禁数组
                        w.WriteNullValue();
                        return;
                    }
                    w.WriteStartArray();
                    foreach (var item in seq.Elements) WriteSerilogValue(w, item);
                    w.WriteEndArray();
                    return;

                default:
                    w.WriteStringValue(value.ToString());
                    return;
            }
        }

        private static void WriteScalar(Utf8JsonWriter w, object? raw)
        {
            switch (raw)
            {
                case null: w.WriteNullValue(); return;
                case string s: w.WriteStringValue(s); return;
                case bool b: w.WriteBooleanValue(b); return;
                case int i: w.WriteNumberValue(i); return;
                case long l: w.WriteNumberValue(l); return;
                case short sh: w.WriteNumberValue(sh); return;
                case byte by: w.WriteNumberValue(by); return;
                case uint ui: w.WriteNumberValue(ui); return;
                case ulong ul: w.WriteNumberValue(ul); return;
                case double d:
                    // net472 没有 double.IsFinite，用 !IsNaN && !IsInfinity 等价替代。
                    if (!double.IsNaN(d) && !double.IsInfinity(d)) w.WriteNumberValue(d);
                    else w.WriteStringValue(d.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case float f:
                    if (!float.IsNaN(f) && !float.IsInfinity(f)) w.WriteNumberValue(f);
                    else w.WriteStringValue(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case decimal dec: w.WriteNumberValue(dec); return;
                case DateTime dt: w.WriteStringValue(dt.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture)); return;
                case DateTimeOffset dto: w.WriteStringValue(dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture)); return;
                case Guid g: w.WriteStringValue(g.ToString()); return;
                default: w.WriteStringValue(raw.ToString() ?? string.Empty); return;
            }
        }
    }
}
