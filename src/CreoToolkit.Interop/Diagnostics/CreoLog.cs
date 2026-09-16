using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using CreoToolkit.Interop.Diagnostics.Serilog;
using global::Serilog;
using global::Serilog.Context;
using global::Serilog.Core;
using global::Serilog.Events;

namespace CreoToolkit.Interop.Diagnostics;

/// <summary>日志级别，序：Error(0) &lt; Warn(1) &lt; Info(2) &lt; Trace(3)。</summary>
public enum CreoLogLevel
{
    Error = 0,
    Warn = 1,
    Info = 2,
    Trace = 3,
}

/// <summary>架构层标识</summary>
public enum CreoLogLayer { L1, L2, L3, L4, App }

/// <summary>日志输出格式</summary>
public enum CreoLogFormat { Text, Json, Both }

/// <summary>
/// L2 统一日志门面（Serilog 引擎）。
/// 开关 env CTK_DOTNET_LOG（off/on，默认 on），旧 CTK_LOG 仍 fallback。
/// 级别 env CTK_DOTNET_LOG_LEVEL（error/warn/info/trace），格式 CTK_DOTNET_LOG_FORMAT（text/json/both）。
/// CTK_ENV 基线（development/production）控制默认级别与格式。
/// 默认目录：logs/dotnet/creo-{yyyyMMdd}-{HHmmss}-{pid}.log（首次写入时惰性生成）。
/// SetSink 注入测试 sink 后，每条事件以 JSONL（json/both）+ text 形态分别 callback。线程安全。
/// </summary>
public static class CreoLog
{
    private static readonly object Gate = new object();

    // Serilog 引擎实例（lazy init）
    private static ILogger? _serilog;

    // 当前已绑定的文件 base path（用于 SetFile 切换时归档判定）
    private static string? _currentBasePath;

    // 测试/外部注入的 sink；非 null 时取代默认 Serilog 输出。
    private static Action<string>? _externalSink;

    private static bool _enabled;
    private static CreoLogLevel _threshold;
    private static CreoLogFormat _format = CreoLogFormat.Text;
    private static int _retentionDays = 14;

    // 用户可选 file path 提示（来自 SetFile/env）。null=用默认目录。
    private static string? _fileBaseHint;
    private static bool _fileDailyRolling = true;

    // AsyncLocal corr 作用域关联 ID
    private static readonly AsyncLocal<string?> _corr = new();

    // 幂等归档保护：同一进程内已绑定过的 base 路径（归一化后）不重复归档。
    private static readonly HashSet<string> _setFileBoundBases = new(StringComparer.OrdinalIgnoreCase);

    // 旧 env 弃用警告幂等
    private static readonly HashSet<string> _deprecatedEnvWarned = new(StringComparer.Ordinal);

    // 显式覆盖标记：一旦用户通过属性赋值，env 不再回灌该字段。
    private static bool _enabledOverridden;
    private static bool _thresholdOverridden;
    private static bool _retentionOverridden;
    private static bool _formatOverridden;

    static CreoLog()
    {
        LoadFromEnvironment();
    }

    /// <summary>总开关。默认随 env CTK_DOTNET_LOG（或旧 CTK_LOG）；显式赋值后以赋值为准。</summary>
    public static bool Enabled
    {
        get { lock (Gate) { return _enabled; } }
        set { lock (Gate) { _enabled = value; _enabledOverridden = true; } }
    }

    /// <summary>级别阈值。</summary>
    public static CreoLogLevel Level
    {
        get { lock (Gate) { return _threshold; } }
        set
        {
            lock (Gate)
            {
                _threshold = value;
                _thresholdOverridden = true;
                SyncBridgeLevelSwitchLocked();
            }
        }
    }

    /// <summary>日志保留天数（&lt;=0 不清理）。</summary>
    public static int RetentionDays
    {
        get { lock (Gate) { return _retentionDays; } }
        set { lock (Gate) { _retentionDays = value; _retentionOverridden = true; } }
    }

    /// <summary>替换 sink；传 null 恢复默认（Serilog 引擎）。便于测试捕获。</summary>
    public static void SetSink(Action<string>? sink)
    {
        lock (Gate)
        {
            _externalSink = sink;
        }
    }

    /// <summary>
    /// 设置文件输出 base hint。
    /// dailyRolling=true: 实际文件名 = stem-yyyyMMdd-HHmmss-pid.ext（启动一次性敲定）。
    /// dailyRolling=false: 按 basePath 字面路径写入（缺扩展名时补 .log）。
    /// 已含完整时间戳后缀的路径按原样使用。多次 SetFile 切换路径时，
    /// 旧文件归档到同目录 archive/ 子目录。传 null 关闭文件输出。
    /// </summary>
    public static void SetFile(string? basePath, bool dailyRolling = true)
    {
        lock (Gate)
        {
            // 关闭旧 Serilog 实例
            DisposeSerilogLocked();

            if (string.IsNullOrEmpty(basePath))
            {
                _fileBaseHint = null;
                _currentBasePath = null;
                _fileDailyRolling = true;
                return;
            }

            // 切换 base：把旧 base 已存在文件归档
            string normNew;
            try { normNew = Path.GetFullPath(basePath!); }
            catch { normNew = basePath!; }

            if (dailyRolling && !_setFileBoundBases.Contains(normNew))
            {
                _setFileBoundBases.Add(normNew);
                ArchiveLatest(basePath!);
            }

            // 触发 archive/ 子目录的过期清理（按 mtime + RetentionDays）
            if (dailyRolling)
                CleanupArchiveDir(basePath!);

            _fileBaseHint = basePath;
            _currentBasePath = basePath;
            _fileDailyRolling = dailyRolling;
            // 不立即构建 Serilog —— 留给首次写入惰性触发
        }
    }

    /// <summary>关闭文件输出（等价 SetFile(null)）。</summary>
    public static void CloseFile() => SetFile(null);

    // 关闭并释放 Serilog 实例（持锁）
    private static void DisposeSerilogLocked()
    {
        if (_serilog is IDisposable d)
        {
            try { d.Dispose(); } catch { /* 吞错 */ }
        }
        _serilog = null;
    }

    // 清理 archive/ 子目录中 mtime 早于 RetentionDays 的文件（RetentionDays<=0 跳过）
    private static void CleanupArchiveDir(string basePath)
    {
        try
        {
            if (_retentionDays <= 0) return;
            string path = basePath;
            if (string.IsNullOrEmpty(Path.GetExtension(path))) path = path + ".log";
            var dir = Path.GetDirectoryName(path) ?? ".";
            var archiveDir = Path.Combine(dir, "archive");
            if (!Directory.Exists(archiveDir)) return;

            var cutoff = DateTime.Now.AddDays(-_retentionDays);
            string[] files;
            try { files = Directory.GetFiles(archiveDir, "*", SearchOption.TopDirectoryOnly); }
            catch { return; }
            foreach (var f in files)
            {
                try
                {
                    if (File.GetLastWriteTime(f) < cutoff) File.Delete(f);
                }
                catch { /* 单文件失败不影响其他 */ }
            }
        }
        catch { /* 整体失败不致命 */ }
    }

    // 若 basePath 存在且非空，将其移动到 archive/ 子目录。失败静默 fallback。
    private static void ArchiveLatest(string basePath)
    {
        try
        {
            // 路径规范化：补 .log 扩展
            string path = basePath;
            if (string.IsNullOrEmpty(Path.GetExtension(path)))
                path = path + ".log";

            if (!File.Exists(path)) return;
            var fi = new FileInfo(path);
            if (fi.Length == 0) return;

            var dir = Path.GetDirectoryName(path) ?? ".";
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            var mtime = fi.LastWriteTime;
            var stamp = mtime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
#if NET6_0_OR_GREATER
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
#else
            var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
#endif
            var archiveName = $"{stem}-{stamp}-{pid}{ext}";
            var archiveDir = Path.Combine(dir, "archive");
            Directory.CreateDirectory(archiveDir);
            var archivePath = Path.Combine(archiveDir, archiveName);

            try { File.Move(path, archivePath); }
            catch (IOException) { /* 并发或占用 */ }
            catch (UnauthorizedAccessException) { /* 权限 */ }
        }
        catch
        {
            // 整体失败不致命
        }
    }

    /// <summary>
    /// 重新读取 env（CTK_DOTNET_LOG / CTK_DOTNET_LOG_LEVEL / CTK_DOTNET_LOG_FORMAT / CTK_DOTNET_LOG_FILE /
    /// CTK_DOTNET_LOG_RETENTION_DAYS / CTK_ENV）。旧 CTK_LOG_* 仍作 fallback，每个旧 env 触发一次 deprecated WARN。
    /// 已被属性显式覆盖的字段不会被回灌。
    /// </summary>
    public static void LoadFromEnvironment()
    {
        lock (Gate)
        {
            // ---- 开关 ----
            if (!_enabledOverridden)
            {
                var sw = ReadEnvWithFallback("CTK_DOTNET_LOG", "CTK_LOG");
                _enabled = !(string.Equals(sw, "off", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(sw, "0", StringComparison.Ordinal));
            }

            string? ctkEnv = Environment.GetEnvironmentVariable("CTK_ENV");
            string? explicitLevel = ReadEnvWithFallback("CTK_DOTNET_LOG_LEVEL", "CTK_LOG_LEVEL");
            string? explicitFormat = ReadEnvWithFallback("CTK_DOTNET_LOG_FORMAT", "CTK_LOG_FORMAT");

            if (!_thresholdOverridden)
            {
                if (explicitLevel != null) _threshold = ParseLevel(explicitLevel);
                else if (ctkEnv == "development") _threshold = CreoLogLevel.Trace;
                else if (ctkEnv == "production") _threshold = CreoLogLevel.Warn;
                else _threshold = CreoLogLevel.Info;
                SyncBridgeLevelSwitchLocked();
            }

            if (!_formatOverridden)
            {
                if (explicitFormat != null) _format = ParseFormat(explicitFormat);
                else if (ctkEnv == "development") _format = CreoLogFormat.Both;
                else if (ctkEnv == "production") _format = CreoLogFormat.Json;
                else _format = CreoLogFormat.Text;
            }

            if (!_retentionOverridden)
            {
                _retentionDays = 14;
                var rd = ReadEnvWithFallback("CTK_DOTNET_LOG_RETENTION_DAYS", "CTK_LOG_RETENTION_DAYS");
                if (!string.IsNullOrEmpty(rd)
                    && int.TryParse(rd, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
                {
                    _retentionDays = days;
                }
            }

            // 文件 base hint（env）
            var envFile = ReadEnvWithFallback("CTK_DOTNET_LOG_FILE", "CTK_LOG_FILE");
            if (!string.IsNullOrEmpty(envFile) && _fileBaseHint is null)
            {
                _fileBaseHint = envFile;
            }
        }
    }

    // 读 env：新名优先，旧名 fallback；命中旧名时单次 deprecated WARN。
    private static string? ReadEnvWithFallback(string newName, string oldName)
    {
        var v = Environment.GetEnvironmentVariable(newName);
        if (!string.IsNullOrEmpty(v)) return v;
        var ov = Environment.GetEnvironmentVariable(oldName);
        if (string.IsNullOrEmpty(ov)) return null;
        EmitDeprecatedEnvWarn(oldName, newName);
        return ov;
    }

    // 输出一行 deprecated WARN（同名只触发一次）。在 lock 内调用：避免重入 LoadFromEnvironment。
    private static void EmitDeprecatedEnvWarn(string oldName, string newName)
    {
        if (!_deprecatedEnvWarned.Add(oldName)) return;
        // 延迟到首次实际写入时输出，避免 LoadFromEnvironment 的初始化期触发 lazy init 死循环。
        // 直接通过 Console.Error 输出一行 fallback 文本以保证能被观察到；
        // 同时记入待写队列，首次真正写日志时再用 Serilog 路径补一条结构化记录。
        try
        {
            Console.Error.WriteLine($"[ctk_log] WARN config.env.deprecated old={oldName} new={newName}");
        }
        catch { /* 吞错 */ }
        _pendingDeprecationWarns.Add((oldName, newName));
    }

    private static readonly List<(string Old, string New)> _pendingDeprecationWarns = new();

    /// <summary>清除所有属性覆盖。</summary>
    public static void ResetOverrides()
    {
        lock (Gate)
        {
            _enabledOverridden = false;
            _thresholdOverridden = false;
            _retentionOverridden = false;
            _formatOverridden = false;
        }
    }

    // ───────── 日志方法 ─────────

    // layer 可空:不显式传时由 DeriveLayerFromCallerFile 根据 CallerFilePath 路径前缀自动推导,
    // 避免漏传调用 silent 错标成实现位置 L2 而非调用位置。
    // 显式传非 null 时按显式值,作覆盖通道(测试场景可显式写其他 layer 测对应路径)。
    public static void Error(string msg,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        object? err = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Error, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), err, file, line);

    public static void Error(string msg, Exception? ex,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Error, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), ex, file, line);

    public static void Warn(string msg,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        object? err = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Warn, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), err, file, line);

    public static void Info(string msg,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        object? err = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Info, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), err, file, line);

    public static void Trace(string msg,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        object? err = null,
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
        => Write(CreoLogLevel.Trace, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), err, file, line);

    /// <summary>不带 CallerFilePath/CallerLineNumber 自动捕获的写入重载，供 MEL 桥接调用。
    /// MEL/Serilog 桥接 file=""，调用方必须显式传 layer，否则 fallback 到 App。</summary>
    public static void WriteRaw(CreoLogLevel level, string msg,
        string? @event = null,
        string? module = null,
        object? props = null,
        CreoLogLayer? layer = null,
        object? err = null,
        string file = "",
        int line = 0,
        string? loc = null,
        string? corr = null)
        => Write(level, msg, @event, module, props, layer ?? DeriveLayerFromCallerFile(file), err, file, line, loc, corr);

    // 由 [CallerFilePath] 路径前缀映射到架构层,免去业务调用面手填 layer 的认知负担。
    // 显式传 layer 走 null-coalesce 覆盖,测试代码或跨项目桥接可显式指定。
    internal static CreoLogLayer DeriveLayerFromCallerFile(string file)
    {
        if (string.IsNullOrEmpty(file)) return CreoLogLayer.App;
        var p = file.Replace('\\', '/');
        if (ContainsSegment(p, "/CreoToolkit.NativeAbi/") || ContainsSegment(p, "/CreoToolkit.NativeHost/"))
            return CreoLogLayer.L1;
        if (ContainsSegment(p, "/CreoToolkit.Interop/")) return CreoLogLayer.L2;
        if (ContainsSegment(p, "/CreoToolkit.Sdk/")) return CreoLogLayer.L3;
        if (ContainsSegment(p, "/CreoToolkit.Agent/")) return CreoLogLayer.L4;
        if (ContainsSegment(p, "/CreoToolkit.App/")
            || ContainsSegment(p, "/CreoToolkit.Host/")
            || ContainsSegment(p, "/samples/")
            || ContainsSegment(p, "/Samples/"))
            return CreoLogLayer.App;
        // 测试代码默认 fallback 为 L2,与现有 JSONL 测试 assertion 行为一致。
        if (ContainsSegment(p, "/tests/")) return CreoLogLayer.L2;
        return CreoLogLayer.App;
    }

    private static bool ContainsSegment(string source, string segment)
        => source.IndexOf(segment, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// 结构化写入入口（S5 MEL 适配层用）：把 module/event/props/loc 注入 Serilog ForContext 上下文。
    /// </summary>
    internal static void WriteStructured(CreoLogLevel level, string? module, string? @event,
        string msg, object? props = null, string? file = null, int line = 0)
        => Write(level, msg, @event, module, props, CreoLogLayer.L2, err: null,
                 file ?? string.Empty, line);

    private static readonly global::Serilog.Core.LoggingLevelSwitch BridgeLevelSwitch =
        new(MapToSerilogLevel(CreoLogLevel.Trace));

    private static readonly Lazy<ILogger> BridgeRoot = new(
        () => new LoggerConfiguration()
            .MinimumLevel.ControlledBy(BridgeLevelSwitch)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CreoLogBridgeSink())
            .CreateLogger(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static ILogger CreateSerilogLogger(string? module, CreoLogLayer layer)
    {
        lock (Gate)
        {
            SyncBridgeLevelSwitchLocked();
        }

        return BridgeRoot.Value
            .ForContext("module", module)
            .ForContext("layer", LayerName(layer));
    }

    internal static bool IsEnabledFor(CreoLogLevel level)
    {
        lock (Gate)
        {
            return _enabled && (int)level <= (int)_threshold;
        }
    }

    private static void SyncBridgeLevelSwitchLocked()
    {
        BridgeLevelSwitch.MinimumLevel = MapToSerilogLevel(_threshold);
    }

    // ───────── 核心写入 ─────────

    private static void Write(CreoLogLevel level, string msg,
        string? @event, string? module, object? props, CreoLogLayer layer, object? err,
        string file, int lineNo, string? locOverride = null, string? corrOverride = null)
    {
        bool enabled;
        CreoLogLevel threshold;
        CreoLogFormat format;
        Action<string>? externalSink;
        ILogger? logger;
        List<(string Old, string New)> pendingDeprecations;
        lock (Gate)
        {
            if (!_enabled || (int)level > (int)_threshold) return;
            enabled = _enabled;
            threshold = _threshold;
            format = _format;
            externalSink = _externalSink;
            // 外部 sink 模式不需要 Serilog 实例，但 deprecation 仍需 drain 到外部 sink
            logger = externalSink is null ? EnsureSerilogLocked() : null;
            pendingDeprecations = new List<(string, string)>(_pendingDeprecationWarns);
            if (externalSink is not null) _pendingDeprecationWarns.Clear();
        }

        // 外部 sink 模式下，先把 pending deprecation 通过外部 sink 写出
        if (externalSink is not null && pendingDeprecations.Count > 0)
        {
            foreach (var (oldN, newN) in pendingDeprecations)
            {
                EmitToExternalSink(externalSink, format,
                    CreoLogLevel.Warn, CreoLogLayer.App,
                    module: "ctk_log",
                    @event: "config.env.deprecated",
                    msg: $"旧 env {oldN} 已弃用，请改用 {newN}",
                    loc: null, corr: null, tid: Environment.CurrentManagedThreadId,
                    props: new { old = oldN, @new = newN },
                    err: null, exObj: null);
            }
        }

        // 收敛 loc/corr
        string? loc = locOverride ?? (string.IsNullOrEmpty(file)
            ? null
            : $"{Path.GetFileName(file)}:{lineNo}");
        string? corrVal = corrOverride ?? _corr.Value;
        int tid = Environment.CurrentManagedThreadId;

        Exception? exObj = err as Exception;
        object? errPayload = err is Exception ? null : err;

        // 外部 sink 路径：渲染后 callback。json 优先，text 跟随 both 模式。
        if (externalSink is not null)
        {
            EmitToExternalSink(externalSink, format, level, layer, module, @event, msg, loc, corrVal, tid, props, err, exObj);
            return;
        }

        // 默认路径：交给 Serilog 引擎
        // 注意 module/event 即便为 null 也强制 push（让 JsonlFormatter 区分 absent vs explicit null）
        var enriched = logger!;
        enriched = enriched.ForContext("layer", LayerName(layer));
        enriched = enriched.ForContext("module", module);
        enriched = enriched.ForContext("event", @event);
        if (loc is not null) enriched = enriched.ForContext("loc", loc);
        if (corrVal is not null) enriched = enriched.ForContext("corr", corrVal);
        enriched = enriched.ForContext("tid", tid);
        if (props is not null) enriched = enriched.ForContext("props", props, destructureObjects: true);
        if (errPayload is not null)
        {
            enriched = enriched.ForContext("err", errPayload, destructureObjects: true);
        }

        var serilogLevel = MapToSerilogLevel(level);
        if (exObj is not null)
            enriched.Write(serilogLevel, exObj, "{Message}", msg);
        else
            enriched.Write(serilogLevel, "{Message}", msg);
    }

    // 把事件渲染成 text + json 行（按 format）并喂给外部 sink。
    private static void EmitToExternalSink(Action<string> sink, CreoLogFormat format,
        CreoLogLevel level, CreoLogLayer layer, string? module, string? @event,
        string msg, string? loc, string? corr, int tid, object? props, object? err, Exception? exObj)
    {
        string levelStr = LevelName(level);
        string layerStr = LayerName(layer);

        if (format == CreoLogFormat.Text || format == CreoLogFormat.Both)
        {
            var ts = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var locText = loc ?? "?:0";
            try { sink($"[{ts}][{levelStr}][{layerStr}][{locText}] {msg}"); } catch { /* 吞错 */ }
        }

        if (format == CreoLogFormat.Json || format == CreoLogFormat.Both)
        {
            // 直接构造 LogEvent 喂给 JsonlFormatter 保证 schema 一致。
            try
            {
                var props2 = new List<LogEventProperty>();
                props2.Add(new LogEventProperty("layer", new ScalarValue(layerStr)));
                // module/event 强制 push（即便 null），让 JsonlFormatter 区分 absent vs explicit null
                props2.Add(new LogEventProperty("module", new ScalarValue(module)));
                props2.Add(new LogEventProperty("event", new ScalarValue(@event)));
                if (loc is not null) props2.Add(new LogEventProperty("loc", new ScalarValue(loc)));
                if (corr is not null) props2.Add(new LogEventProperty("corr", new ScalarValue(corr)));
                props2.Add(new LogEventProperty("tid", new ScalarValue(tid)));
                if (props is not null)
                {
                    // 把 props 包成 StructureValue
                    var sv = ConvertToStructure(props);
                    if (sv is not null) props2.Add(new LogEventProperty("props", sv));
                }
                if (err is not null && err is not Exception)
                {
                    // 结构化 err(WithError 产物)同样入 property,Exception 本体走 LogEvent.Exception 槽
                    var esv = ConvertToStructure(err);
                    if (esv is not null) props2.Add(new LogEventProperty("err", esv));
                }

                // Serilog 4.x 的 TextToken 是 internal，外部无法 new。用 MessageTemplateParser 解析。
                var template = new global::Serilog.Parsing.MessageTemplateParser().Parse(msg);

                var ev = new LogEvent(
                    DateTimeOffset.Now,
                    MapToSerilogLevel(level),
                    exObj,
                    template,
                    props2);

                var fmt = new JsonlFormatter(layerStr, module);
                using var sw = new StringWriter();
                fmt.Format(ev, sw);
                var jsonLine = sw.ToString().TrimEnd('\n', '\r');
                sink(jsonLine);
            }
            catch
            {
                /* sink 失败不致命 */
            }
        }
    }

    // 把 anonymous/POCO/IDictionary 转成 StructureValue（浅层反射 + IDictionary 处理）。
    private static StructureValue? ConvertToStructure(object obj)
    {
        try
        {
            var props = new List<LogEventProperty>();

            // IDictionary<string, ...> 优先：把 key 当作 prop name
            if (obj is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    var key = kv.Key?.ToString();
                    if (string.IsNullOrEmpty(key)) continue;
                    props.Add(new LogEventProperty(key!, new ScalarValue(kv.Value)));
                }
                return new StructureValue(props);
            }

            // 其它对象走反射
            var ps = obj.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var p in ps)
            {
                object? v;
                try { v = p.GetValue(obj); } catch { v = null; }
                props.Add(new LogEventProperty(p.Name, new ScalarValue(v)));
            }
            return new StructureValue(props);
        }
        catch { return null; }
    }

    // 惰性构建 Serilog 实例（持锁）
    private static ILogger EnsureSerilogLocked()
    {
        if (_serilog is not null) return _serilog;

        string baseHint = _fileBaseHint ?? DefaultBaseHint();
        bool dailyRolling = _fileDailyRolling;
        string filePath;
        try
        {
            filePath = dailyRolling
                ? SerilogBootstrap.BuildLogFilePath(baseHint)
                : NormalizeLiteralLogPath(baseHint);
        }
        catch
        {
            // S4 不可用时退化为本地默认（极少触发）
            filePath = baseHint;
        }

        try
        {
            var options = new SerilogLogOptions(
                FilePath: filePath,
                Level: _threshold,
                Format: _format,
                RetentionDays: _retentionDays,
                Layer: "app");
            _serilog = SerilogBootstrap.BuildLogger(options);

            if (dailyRolling)
            {
                // retention cleanup（构造时 BuildLogger 内已调，这里冗余保险）
                try { SerilogBootstrap.CleanupOldLogs(filePath, _retentionDays); }
                catch { /* 吞错 */ }
            }
        }
        catch
        {
            // 兜底：构造一个 console-only logger
            _serilog = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .CreateLogger();
        }

        // flush 待写 deprecation warning
        DrainPendingDeprecationsLocked();
        return _serilog;
    }

    private static string NormalizeLiteralLogPath(string baseHint)
    {
        if (string.IsNullOrEmpty(baseHint))
            throw new ArgumentException("baseHint 不能为空", nameof(baseHint));

        return string.IsNullOrEmpty(Path.GetExtension(baseHint))
            ? baseHint + ".log"
            : baseHint;
    }

    private static void DrainPendingDeprecationsLocked()
    {
        if (_pendingDeprecationWarns.Count == 0 || _serilog is null) return;
        foreach (var (oldN, newN) in _pendingDeprecationWarns)
        {
            try
            {
                _serilog
                    .ForContext("layer", "app")
                    .ForContext("module", "ctk_log")
                    .ForContext("event", "config.env.deprecated")
                    .ForContext("props", new { old = oldN, @new = newN }, destructureObjects: true)
                    .ForContext("tid", Environment.CurrentManagedThreadId)
                    .Warning("旧 env {OldName} 已弃用，请改用 {NewName}", oldN, newN);
            }
            catch { /* 吞错 */ }
        }
        _pendingDeprecationWarns.Clear();
    }

    // 默认 base hint：logs/dotnet/creo（启动时一次性敲定时间戳-pid）
    private static string DefaultBaseHint()
    {
        return Path.Combine("logs", "dotnet", "creo");
    }

    private static LogEventLevel MapToSerilogLevel(CreoLogLevel level) => level switch
    {
        CreoLogLevel.Error => LogEventLevel.Error,
        CreoLogLevel.Warn  => LogEventLevel.Warning,
        CreoLogLevel.Info  => LogEventLevel.Information,
        CreoLogLevel.Trace => LogEventLevel.Verbose,
        _ => LogEventLevel.Information,
    };

    // ───────── Scope API ─────────

    private sealed class CorrScope : IDisposable
    {
        private readonly string? _prev;
        private readonly IDisposable? _serilogPush;
        public CorrScope(string corr)
        {
            _prev = _corr.Value;
            _corr.Value = corr;
            try { _serilogPush = LogContext.PushProperty("corr", corr); }
            catch { _serilogPush = null; }
        }
        public void Dispose()
        {
            _corr.Value = _prev;
            try { _serilogPush?.Dispose(); } catch { /* 吞错 */ }
        }
    }

    /// <summary>在 using 块内所有 CreoLog 调用自动带 corr 关联 ID。</summary>
    public static IDisposable Scope(string corr) => new CorrScope(corr);

    // ───────── 辅助 ─────────

    /// <summary>从 Exception 构建 err 字段。</summary>
    public static object? WithError(Exception? ex) => ex == null ? null
        : new { code = ex.HResult, type = ex.GetType().FullName, msg = ex.Message, stack = ex.StackTrace };

    private static CreoLogLevel ParseLevel(string? value)
    {
        if (string.IsNullOrEmpty(value)) return CreoLogLevel.Info;
        if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase)) return CreoLogLevel.Error;
        if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase)) return CreoLogLevel.Warn;
        if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase)) return CreoLogLevel.Info;
        if (string.Equals(value, "trace", StringComparison.OrdinalIgnoreCase)) return CreoLogLevel.Trace;
        return CreoLogLevel.Info;
    }

    private static CreoLogFormat ParseFormat(string value) => value.ToLowerInvariant() switch
    {
        "json" => CreoLogFormat.Json,
        "both" => CreoLogFormat.Both,
        _ => CreoLogFormat.Text,
    };

    private static string LevelName(CreoLogLevel level) => level switch
    {
        CreoLogLevel.Error => "ERROR",
        CreoLogLevel.Warn  => "WARN",
        CreoLogLevel.Info  => "INFO",
        CreoLogLevel.Trace => "TRACE",
        _ => "INFO",
    };

    private static string LayerName(CreoLogLayer layer) => layer switch
    {
        CreoLogLayer.L1  => "L1",
        CreoLogLayer.L2  => "L2",
        CreoLogLayer.L3  => "L3",
        CreoLogLayer.L4  => "L4",
        CreoLogLayer.App => "app",
        _ => "L2",
    };

    // ───────── 测试专用 API ─────────

    /// <summary>仅供测试：重置 CreoLog 到指定路径/格式/级别/env 基线。
    /// 写入用自定义 sink 直接 append 到文件，避免 Windows 文件锁。</summary>
    internal static void ResetForTest(string logPath, string? format, string? level, string? ctkEnv)
    {
        lock (Gate)
        {
            DisposeSerilogLocked();
            _setFileBoundBases.Clear();
            _deprecatedEnvWarned.Clear();
            _pendingDeprecationWarns.Clear();
            _fileBaseHint = null;
            _currentBasePath = null;
            _fileDailyRolling = true;

            // 显式参数 > 新 env > 旧 env fallback > ctkEnv 基线 > 默认
            string? envFormat = format ?? ReadEnvWithFallback("CTK_DOTNET_LOG_FORMAT", "CTK_LOG_FORMAT");
            string? envLevel = level ?? ReadEnvWithFallback("CTK_DOTNET_LOG_LEVEL", "CTK_LOG_LEVEL");

            CreoLogFormat resolvedFormat;
            if (envFormat != null) resolvedFormat = ParseFormat(envFormat);
            else if (ctkEnv == "development") resolvedFormat = CreoLogFormat.Both;
            else if (ctkEnv == "production") resolvedFormat = CreoLogFormat.Json;
            else resolvedFormat = CreoLogFormat.Text;
            _format = resolvedFormat;
            _formatOverridden = envFormat != null;

            if (envLevel != null)
            {
                _threshold = ParseLevel(envLevel);
                _thresholdOverridden = true;
            }
            else
            {
                _thresholdOverridden = false;
                if (ctkEnv == "development") _threshold = CreoLogLevel.Trace;
                else if (ctkEnv == "production") _threshold = CreoLogLevel.Warn;
                else _threshold = CreoLogLevel.Info;
            }
            SyncBridgeLevelSwitchLocked();

            _enabled = true;
            _enabledOverridden = true;

            // 注入 sink：每次写用 FileShare.ReadWrite 打开，允许同时读
            string path = logPath;
            _externalSink = line =>
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs, new UTF8Encoding(false));
                    sw.WriteLine(line);
                }
                catch { /* 写失败不致命 */ }
            };
        }
    }

    /// <summary>仅供测试：无需 flush（sink 每次直接写入）。</summary>
    internal static void FlushForTest()
    {
        // sink 每次调用直接落盘
    }
}
