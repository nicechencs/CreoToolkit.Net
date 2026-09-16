using CreoToolkit.App;

namespace CreoToolkit.Diagnostics;

/// <summary>Diagnostic-only startup inputs. Host lifecycle options remain in CreoToolkit.Host.</summary>
public sealed record DiagnosticsStartupOptions(
    string? PreloadModel,
    string? LoadModelPath,
    string? DiagnosticCommand,
    string? RibbonFile)
{
    public static DiagnosticsStartupOptions FromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable);

    internal static DiagnosticsStartupOptions Resolve(Func<string, string?> readEnvironment)
    {
        ThrowUtil.IfNull(readEnvironment);
        var preloadModel = NullIfWhiteSpace(readEnvironment(CtkEnv.AppPreloadModel));
        var loadModelPath = NullIfWhiteSpace(readEnvironment(CtkEnv.AppLoadModelPath));
        var diagnosticCommand = NullIfWhiteSpace(readEnvironment(CtkEnv.AppRunCommand));

        string? ribbonFile = null;
        if (IsEnabled(readEnvironment(CtkEnv.AppLoadRibbon)))
            ribbonFile = NullIfWhiteSpace(readEnvironment(CtkEnv.AppRibbonFile)) ?? "ctk_demo.rbn";

        return new DiagnosticsStartupOptions(preloadModel, loadModelPath, diagnosticCommand, ribbonFile);
    }

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    internal static bool IsEnabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
}
