using CreoToolkit.App;

namespace CreoToolkit.Host;

/// <summary>
/// Host-owned activation seam. Legacy diagnostic environment variables opt in
/// automatically; CTK_DIAGNOSTICS_ENABLED=0 is an explicit bypass.
/// </summary>
internal readonly record struct DiagnosticsActivation(bool Requested, bool Required)
{
    internal static DiagnosticsActivation FromEnvironment()
        => Resolve(Environment.GetEnvironmentVariable);

    internal static DiagnosticsActivation Resolve(Func<string, string?> readEnvironment)
    {
        ThrowUtil.IfNull(readEnvironment);
        var enabled = readEnvironment(CtkEnv.DiagnosticsEnabled);
        if (IsDisabled(enabled))
            return new DiagnosticsActivation(false, false);

        var required = IsEnabled(readEnvironment(CtkEnv.DiagnosticsRequired));
        var legacyRequested =
            !string.IsNullOrWhiteSpace(readEnvironment(CtkEnv.AppPreloadModel)) ||
            !string.IsNullOrWhiteSpace(readEnvironment(CtkEnv.AppLoadModelPath)) ||
            !string.IsNullOrWhiteSpace(readEnvironment(CtkEnv.AppRunCommand)) ||
            IsEnabled(readEnvironment(CtkEnv.AppLoadRibbon));
        return new DiagnosticsActivation(required || IsEnabled(enabled) || legacyRequested, required);
    }

    private static bool IsEnabled(string? value)
        => string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabled(string? value)
        => string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
           || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
}
