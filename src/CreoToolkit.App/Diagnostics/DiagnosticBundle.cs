using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CreoToolkit.App.Diagnostics;

/// <summary>
/// 诊断包 — 一键采集 host log + 模块版本 + env vars + protk.dat 打成 zip 给工程师报障。
/// 单一 lib + options + 默认安全 (脱敏/大小上限/zip-slip 防御)。
/// </summary>
public static class DiagnosticBundle
{
    // 诊断包要 dump 到 manifest 的 CTK_* env vars 白名单。
    // 字符串字面已抽到 CtkEnv,本数组转引 CtkEnv.All 单一真源。
    private static readonly IReadOnlyList<string> CtkEnvVarNames = CtkEnv.All;

    /// <summary>
    /// 采集诊断包,写 zip 到 <see cref="DiagnosticBundleOptions.OutputZipPath"/>;
    /// 返回 manifest JSON 字符串(摘要,可立刻 log/UI 展示)。
    /// 异常处理: 单 item 失败进 manifest.errors[],不阻塞其他 item,zip 仍可生成。
    /// </summary>
    public static string Collect(DiagnosticBundleOptions options)
    {
        ThrowUtil.IfNull(options);
        if (string.IsNullOrWhiteSpace(options.OutputZipPath))
            throw new ArgumentException("OutputZipPath is required.", nameof(options));

        var redactor = new PathRedactor(enabled: options.RedactPaths);
        var errors = new List<object>();
        var fileEntries = new List<object>();

        var outZip = Path.GetFullPath(options.OutputZipPath);
        var outDir = Path.GetDirectoryName(outZip);
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        using (var fileStream = new FileStream(outZip, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false))
        {
            // 1. 收 3 个 log + AdditionalFiles → logs/* 或 extra/*
            var fileSources = EnumerateFileSources(options);
            foreach (var (entryName, srcPath) in fileSources)
            {
                try
                {
                    var safe = SafeZipEntryName(entryName);
                    var meta = CopyFileToZip(zip, safe, srcPath, options.MaxLogBytes, redactor);
                    fileEntries.Add(meta);
                }
                catch (Exception ex)
                {
                    errors.Add(new { step = $"collect_file:{entryName}", message = ex.Message });
                    fileEntries.Add(new
                    {
                        name = SafeZipEntryName(entryName),
                        status = "error",
                        path = redactor.Redact(srcPath),
                        error = ex.Message,
                    });
                }
            }

            // 2. protk.dat 探测(Environment.CurrentDirectory + AppContext.BaseDirectory)
            try
            {
                var protkPath = FindProtkDat();
                if (protkPath is not null)
                {
                    var meta = CopyFileToZip(zip, "protk.dat", protkPath, options.MaxLogBytes, redactor);
                    fileEntries.Add(meta);
                }
                else
                {
                    fileEntries.Add(new { name = "protk.dat", status = "missing" });
                }
            }
            catch (Exception ex)
            {
                errors.Add(new { step = "find_protk", message = ex.Message });
            }

            // 3. module 版本表
            var modules = CollectModuleVersions(errors, redactor);

            // 4. env vars
            var envVars = CollectCtkEnvVars(redactor);

            // 5. manifest.json (最后写,确保 partial collection 也可读)
            var manifest = new
            {
                tool_version = typeof(DiagnosticBundle).Assembly.GetName().Version?.ToString() ?? "unknown",
                timestamp = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                env_vars = envVars,
                modules,
                files = fileEntries,
                errors,
            };

            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(manifestJson);
            }

            return manifestJson;
        }
    }

    // ============ private helpers ============

    /// <summary>消费者源：managed log(env 优先) + native-bootstrap JSONL(pointer 定位)。
    /// host-marker / host-native / latest.json 写入已退役,native-bootstrap JSONL 是唯一真源。</summary>
    private static IEnumerable<(string EntryName, string SrcPath)> EnumerateFileSources(DiagnosticBundleOptions options)
    {
        // managed log
        var managedDefault = Path.Combine(AppContext.BaseDirectory, "logs", "host-managed.log");
        var managed = Environment.GetEnvironmentVariable(CtkEnv.HostManagedLog) ?? managedDefault;
        yield return ("logs/host-managed.log", managed);

        // native-bootstrap JSONL：读 latest-session.pointer 定位当前 session 文件
        var bootstrapJsonl = ResolveBootstrapJsonl();
        yield return ("logs/native-bootstrap.jsonl", bootstrapJsonl);

        // managed rolled logs
        foreach (var rolled in FindRolledLogs(managed))
            yield return ($"logs/managed-rolled/{Path.GetFileName(rolled)}", rolled);

        // native-bootstrap 历史：同目录下所有 native-bootstrap-*.jsonl（除当前 session）
        foreach (var older in FindOlderBootstrapJsonl(bootstrapJsonl))
            yield return ($"logs/bootstrap-history/{Path.GetFileName(older)}", older);

        // AdditionalFiles 扩展口
        if (options.AdditionalFiles is not null)
        {
            foreach (var extra in options.AdditionalFiles)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                yield return ($"extra/{Path.GetFileName(extra)}", extra);
            }
        }
    }

    /// <summary>定位当前 session 的 native-bootstrap JSONL 文件。
    /// 读 latest-session.pointer 获取文件名,与 host log dir 拼接为绝对路径。
    /// 决策树与 native 端 host_log_resolve_dir 对齐。</summary>
    private static string ResolveBootstrapJsonl()
    {
        try
        {
            foreach (var dir in EnumerateCandidateHostLogDirs())
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                var pointerPath = Path.Combine(dir!, "latest-session.pointer");
                if (!File.Exists(pointerPath)) continue;
                var fileName = File.ReadAllText(pointerPath).Trim();
                if (string.IsNullOrEmpty(fileName)) continue;
                var jsonlPath = Path.Combine(dir!, fileName);
                if (File.Exists(jsonlPath)) return jsonlPath;
            }
        }
        catch { }
        return string.Empty;
    }

    /// <summary>同目录下除当前 session 外的历史 native-bootstrap-*.jsonl 文件。</summary>
    private static IEnumerable<string> FindOlderBootstrapJsonl(string currentJsonl)
    {
        if (string.IsNullOrEmpty(currentJsonl)) yield break;
        var dir = Path.GetDirectoryName(currentJsonl);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
        var currentName = Path.GetFileName(currentJsonl);
        string[] files;
        try { files = Directory.GetFiles(dir!, "native-bootstrap-*.jsonl"); }
        catch { yield break; }
        foreach (var f in files)
        {
            if (!string.Equals(Path.GetFileName(f), currentName, StringComparison.OrdinalIgnoreCase))
                yield return f;
        }
    }

    private static IEnumerable<string?> EnumerateCandidateHostLogDirs()
    {
        // 1. env CTK_BOOTSTRAP_LOG_DIR / legacy CTK_HOST_LOG_DIR
        yield return Environment.GetEnvironmentVariable(CtkEnv.BootstrapLogDir);
#pragma warning disable CS0618 // 诊断包必须继续探测 deprecated 兼容名
        yield return Environment.GetEnvironmentVariable(CtkEnv.HostLogDir);
#pragma warning restore CS0618

        // 2-3. AppContext.BaseDirectory / ProcessPath 各上行 4 级 + logs\host
        foreach (var dir in DeriveLogsHostCandidates(AppContext.BaseDirectory)) yield return dir;
        string? processDir = null;
        try { processDir = Path.GetDirectoryName((System.Diagnostics.Process.GetCurrentProcess().MainModule != null ? System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName : null)); } catch { }
        foreach (var dir in DeriveLogsHostCandidates(processDir)) yield return dir;

        // 4. %LOCALAPPDATA%\CreoToolkit\logs\host\
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            yield return Path.Combine(localAppData, "CreoToolkit", "logs", "host");
    }

    private static IEnumerable<string?> DeriveLogsHostCandidates(string? baseDir)
    {
        if (string.IsNullOrEmpty(baseDir)) yield break;
        string? current;
        try { current = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { yield break; }

        for (int i = 0; i < 5 && !string.IsNullOrEmpty(current); i++)
        {
            string? candidate = null;
            try { candidate = Path.Combine(current, "logs", "host"); } catch { }
            if (!string.IsNullOrEmpty(candidate)) yield return candidate;
            try { current = Path.GetDirectoryName(current); } catch { current = null; }
        }
    }

    // managed log 归档在 <dir>/archive/ 子目录
    private static IEnumerable<string> FindRolledLogs(string baseFilePath)
    {
        if (string.IsNullOrEmpty(baseFilePath)) yield break;
        var dir = Path.GetDirectoryName(baseFilePath);
        if (string.IsNullOrEmpty(dir)) yield break;
        var archiveDir = Path.Combine(dir!, "archive");
        if (!Directory.Exists(archiveDir)) yield break;
        var stem = Path.GetFileNameWithoutExtension(baseFilePath);
        var ext = Path.GetExtension(baseFilePath);
        var pattern = $"{stem}-*{ext}";
        foreach (var f in Directory.EnumerateFiles(archiveDir, pattern))
            yield return f;
    }

    /// <summary>zip entry 安全名: 拒绝 `..` / 绝对路径 / UNC / Windows 盘符,统一正斜杠相对名(zip-slip 防御)。
    /// <see cref="Path.IsPathRooted"/> 跨 OS 行为不一致,故显式判多种绝对路径形态。</summary>
    internal static string SafeZipEntryName(string entry)
    {
        if (string.IsNullOrEmpty(entry))
            throw new ArgumentException("entry empty");
        // 显式拒所有绝对路径形态(跨 OS 一致)
        if (entry.StartsWith(@"\\") || entry.StartsWith("//"))           // UNC \\server\share or //server/share
            throw new ArgumentException($"UNC path rejected: {entry}");
        if (entry.StartsWith("/") || entry.StartsWith("\\"))             // unix/windows root
            throw new ArgumentException($"absolute path rejected: {entry}");
        if (entry.Length >= 2 && entry[1] == ':')                        // Windows drive letter C:\foo or C:/foo
            throw new ArgumentException($"drive-letter path rejected: {entry}");
        if (Path.IsPathRooted(entry))                                    // 兜底,跨平台
            throw new ArgumentException($"rooted path rejected: {entry}");
        // 统一分隔符
        var n = entry.Replace('\\', '/').TrimStart('/');
        // 拒绝 .. traversal
        if (n.Split('/').Any(seg => seg == ".."))
            throw new ArgumentException($"path traversal rejected: {entry}");
        return n;
    }

    private static object CopyFileToZip(
        ZipArchive zip, string entryName, string srcPath, long maxBytes, PathRedactor redactor)
    {
        if (!File.Exists(srcPath))
        {
            return new
            {
                name = entryName,
                status = "missing",
                path = redactor.Redact(srcPath),
            };
        }

        var fi = new FileInfo(srcPath);
        var originalSize = fi.Length;
        var truncated = false;
        var bytesToCopy = originalSize;
        if (maxBytes > 0 && originalSize > maxBytes)
        {
            bytesToCopy = maxBytes;
            truncated = true;
        }

        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var inStream = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (truncated)
        {
            // 保留尾部最近的日志(tail)
            inStream.Seek(originalSize - bytesToCopy, SeekOrigin.Begin);
        }
        using var outStream = entry.Open();
        var buffer = new byte[81920];
        long remaining = bytesToCopy;
        int read;
        while (remaining > 0 && (read = inStream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            outStream.Write(buffer, 0, read);
            remaining -= read;
        }

        return new
        {
            name = entryName,
            status = truncated ? "truncated" : "collected",
            size = bytesToCopy,
            original_size = originalSize,
            truncate_from = truncated ? "tail" : null,   // 截断方向显式标注
            path = redactor.Redact(srcPath),
        };
    }

    /// <summary>探测顺序加 env CTK_HOST_PROTK_DAT(完整路径) + CTK_HOST_PROTK_DAT_DIR(目录),
    /// 找不到再回退 CurrentDirectory / BaseDirectory(默认场景仍可用,客户可自定义)。</summary>
    private static string? FindProtkDat()
    {
        var explicitPath = Environment.GetEnvironmentVariable(CtkEnv.HostProtkDat);
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
            return explicitPath;

        var candidates = new[]
        {
            Environment.GetEnvironmentVariable(CtkEnv.HostProtkDatDir),
            Environment.CurrentDirectory,
            AppContext.BaseDirectory,
        };
        foreach (var dir in candidates)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var candidate = Path.Combine(dir, "protk.dat");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static List<object> CollectModuleVersions(List<object> errors, PathRedactor redactor)
    {
        var modules = new List<object>();
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var name = asm.GetName();
                    string? location = null;
                    string? fileVersion = null;
                    try { location = asm.Location; } catch { }
                    if (!string.IsNullOrEmpty(location) && File.Exists(location))
                    {
                        try { fileVersion = FileVersionInfo.GetVersionInfo(location).FileVersion; } catch { }
                    }
                    modules.Add(new
                    {
                        name = name.Name,
                        version = name.Version?.ToString(),
                        file_version = fileVersion,
                        location = redactor.Redact(location),
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(new { step = $"collect_module", message = ex.Message });
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add(new { step = "enumerate_assemblies", message = ex.Message });
        }
        return modules;
    }

    private static Dictionary<string, string?> CollectCtkEnvVars(PathRedactor redactor)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in CtkEnvVarNames)
        {
            var value = Environment.GetEnvironmentVariable(name);
            result[name] = redactor.Redact(value);
        }
        return result;
    }

    /// <summary>路径脱敏: 绝对路径统一替换 <c>&lt;PATH-N&gt;</c>,同一路径同一编号,保留相对结构线索给工程师。
    /// 正则覆盖 UNC + Windows 盘符 + Unix 绝对路径。URL 中的路径段可能被误抓 — 当前 Module 不会 dump URL,可接受。</summary>
    internal sealed class PathRedactor
    {
        private readonly bool _enabled;
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex AbsolutePathRegex = new(
            @"\\\\[^\s""<>|*?]+\\[^\s""<>|*?]+(?:\\[^\s""<>|*?]+)*" +  // UNC \\server\share\path
            @"|[A-Za-z]:\\[^""\s<>|*?]*" +                              // Windows C:\foo\bar
            @"|/(?:[^/\s""<>|*?]+/)+[^/\s""<>|*?]*",                    // Unix /foo/bar/baz
            RegexOptions.Compiled);

        public PathRedactor(bool enabled) => _enabled = enabled;

        public string? Redact(string? input)
        {
            if (!_enabled || string.IsNullOrEmpty(input)) return input;
            return AbsolutePathRegex.Replace(input, m =>
            {
                var key = m.Value;
                if (!_map.TryGetValue(key, out var token))
                {
                    token = $"<PATH-{_map.Count + 1}>";
                    _map[key] = token;
                }
                return token;
            });
        }
    }
}
