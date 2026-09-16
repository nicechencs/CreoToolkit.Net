using System.Reflection;
using CreoToolkit.App;
using CreoToolkit.Interop;
using CreoToolkit.Sdk;
using System.Linq;

namespace CreoToolkit.Host;

/// <summary>
/// Host 侧应用装配器。未设置环境变量时返回 <see cref="NoOpApp"/>(空命令集,保留启动语义);
/// 设置 <see cref="AssemblyEnv"/> + <see cref="TypeEnv"/> 时在默认 AppDomain 中 LoadFrom 外部 ICreoApplication。
/// </summary>
public static class CreoApplicationLoader
{
    public const string AssemblyEnv = "CTK_APP_ASSEMBLY";
    public const string TypeEnv = "CTK_APP_TYPE";
    private static int _resolveHooked;

    public static ICreoApplication CreateFromEnvironment(Action<string>? log = null)
        => CreateLoadedFromEnvironment(log).App;

    public static ICreoApplication Create(string? assemblyPath, string? typeName, Action<string>? log = null)
        => CreateLoaded(assemblyPath, typeName, log).App;

    public static LoadedApplication CreateLoadedFromEnvironment(Action<string>? log = null)
        => CreateLoaded(Environment.GetEnvironmentVariable(AssemblyEnv), Environment.GetEnvironmentVariable(TypeEnv), log);

    public static LoadedApplication CreateLoaded(string? assemblyPath, string? typeName, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) && string.IsNullOrWhiteSpace(typeName))
        {
            log?.Invoke("ApplicationLoader: no external app configured; using built-in NoOpApp (空命令集).");
            return new LoadedApplication(new NoOpApp());
        }

        if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(typeName))
            throw new InvalidOperationException($"{AssemblyEnv} and {TypeEnv} must be set together.");

        var baseDir = ResolveBaseDirectory(log);
        var fullPath = ResolvePath(assemblyPath, baseDir);
        ValidateAppAssemblyPath(fullPath, baseDir, log);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Application assembly not found: '{fullPath}'.", fullPath);
        log?.Invoke($"ApplicationLoader: loading '{typeName}' from '{fullPath}'.");

        EnsureSharedAssemblyResolve();
        var assembly = Assembly.LoadFrom(fullPath);
        var type = assembly.GetType(typeName, throwOnError: true)!;
        if (!typeof(ICreoApplication).IsAssignableFrom(type))
            throw new InvalidOperationException($"Type '{typeName}' does not implement {nameof(ICreoApplication)}.");

        if (Activator.CreateInstance(type) is not ICreoApplication app)
            throw new InvalidOperationException($"Type '{typeName}' must have a public parameterless constructor.");

        return new LoadedApplication(app);
    }

    /// <summary>
    /// Fusion 默认 ApplicationBase 是 xtop.exe 目录，看不到 deploy/managed 里的
    /// System.Memory / DiagnosticSource 等 NuGet 程序集。先复用已加载副本，
    /// 再从 Host/Sdk/Interop 所在目录 LoadFrom。
    /// </summary>
    public static void EnsureSharedAssemblyResolve()
    {
        if (Interlocked.CompareExchange(ref _resolveHooked, 1, 0) != 0)
            return;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            AssemblyName requested;
            try { requested = new AssemblyName(args.Name); }
            catch { return null; }
            if (string.IsNullOrEmpty(requested.Name) ||
                requested.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (AssemblyName.ReferenceMatchesDefinition(loaded.GetName(), requested))
                    return loaded;
            }

            foreach (var dir in ProbeDirectories())
            {
                var candidate = Path.Combine(dir, requested.Name + ".dll");
                if (!File.Exists(candidate))
                    continue;
                try { return Assembly.LoadFrom(candidate); }
                catch { /* 继续试下一个目录 */ }
            }

            return null;
        };
    }

    private static IEnumerable<string> ProbeDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[]
                 {
                     Path.GetDirectoryName(typeof(CreoApplicationLoader).Assembly.Location),
                     Path.GetDirectoryName(typeof(CreoSession).Assembly.Location),
                     Path.GetDirectoryName(typeof(CtkAbi).Assembly.Location),
                     Path.GetDirectoryName(Environment.GetEnvironmentVariable(CtkEnv.HostAssembly) ?? string.Empty),
                 })
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            string full;
            try { full = Path.GetFullPath(raw); }
            catch { continue; }
            if (seen.Add(full))
                yield return full;
        }
    }

    private static void ValidateAppAssemblyPath(string fullPath, string baseDir, Action<string>? log)
    {
        var allowedRoots = new List<string>
        {
            NormalizeRoot(baseDir),
        };

        var extra = Environment.GetEnvironmentVariable(CtkEnv.AppAllowedRoots);
        if (!string.IsNullOrWhiteSpace(extra))
        {
            foreach (var root in extra.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = root.Trim();
                if (trimmed.Length == 0) continue;
                try
                {
                    allowedRoots.Add(NormalizeRoot(trimmed));
                }
                catch (Exception ex)
                {
                    log?.Invoke($"ApplicationLoader: ignore invalid CTK_APP_ALLOWED_ROOTS entry '{root}': {ex.Message}");
                }
            }
        }

        var matched = allowedRoots.Any(r => fullPath.StartsWith(r, StringComparison.OrdinalIgnoreCase));
        if (!matched)
        {
            var rootsStr = string.Join("; ", allowedRoots);
            log?.Invoke($"ApplicationLoader: REJECTED - path not in allowlist: {fullPath}; allowed: {rootsStr}");
            throw new InvalidOperationException(
                $"{AssemblyEnv} path not allowlisted: '{fullPath}'. " +
                $"Allowed roots: {rootsStr}. Set CTK_APP_ALLOWED_ROOTS env (';' separated) to add custom paths.");
        }

        log?.Invoke($"ApplicationLoader: location policy passed: {fullPath}");
    }

    private static string ResolveBaseDirectory(Action<string>? log)
    {
        var baseDir = AppContext.BaseDirectory;
        if (string.IsNullOrEmpty(baseDir))
        {
            baseDir = Path.GetDirectoryName(typeof(CreoApplicationLoader).Assembly.Location);
            log?.Invoke($"ApplicationLoader: AppContext.BaseDirectory 空,fallback Assembly.Location 目录: '{baseDir}'");
        }

        return string.IsNullOrEmpty(baseDir)
            ? throw new InvalidOperationException("无法确定 baseDir(BaseDirectory 与 Assembly.Location 均空)。")
            : Path.GetFullPath(baseDir);
    }

    private static string ResolvePath(string path, string baseDir)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path);
        return Path.GetFullPath(Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(baseDir, expanded));
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
}
