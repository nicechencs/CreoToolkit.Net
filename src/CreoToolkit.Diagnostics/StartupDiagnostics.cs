using CreoToolkit.App;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;

namespace CreoToolkit.Diagnostics;

/// <summary>
/// Optional startup diagnostics entry. This project deliberately has no dependency on
/// CreoToolkit.Host; Host calls these two narrow synchronous entry points.
/// </summary>
public static class StartupDiagnostics
{
    public static int PrepareSession(
        CreoSession session,
        bool required,
        Action<string, CreoLogLevel> log)
    {
        ThrowUtil.IfNull(session);
        ThrowUtil.IfNull(log);
        var options = DiagnosticsStartupOptions.FromEnvironment();
        var failed = false;

        if (options.PreloadModel is { } preload)
        {
            try
            {
                var model = session.Models.Retrieve(preload, CreoModelType.Part);
                log($"InitializeApp: preloaded '{model.Name}'; Models.GetCurrent() = {session.Models.GetCurrent()?.Name ?? "(none)"}", CreoLogLevel.Info);
            }
            catch (Exception ex)
            {
                failed = true;
                log($"InitializeApp: preload '{preload}' FAILED: {ex.Message}", CreoLogLevel.Error);
            }
        }

        if (options.LoadModelPath is { } loadPath)
        {
            log($"InitializeApp: CTK_APP_LOAD_MODEL_PATH='{loadPath}'", CreoLogLevel.Info);
            try
            {
                var loaded = session.Models.Load(loadPath, InferModelTypeFromPath(loadPath));
                log($"InitializeApp: loaded '{loaded?.Name}' from '{loadPath}'; Current = {session.Models.GetCurrent()?.Name ?? "(none)"}", CreoLogLevel.Info);
                if (loaded is not null)
                {
                    try
                    {
                        var windowId = session.Run(native => native.ModelWindowOpen(loaded.Identity));
                        log($"InitializeApp: opened window {windowId} for '{loaded.Name}'; Current = {session.Models.GetCurrent()?.Name ?? "(none)"}", CreoLogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        failed = true;
                        log($"InitializeApp: open window for '{loaded.Name}' FAILED: {ex.Message}", CreoLogLevel.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                failed = true;
                log($"InitializeApp: load '{loadPath}' FAILED: {ex.Message}", CreoLogLevel.Error);
            }
        }
        else
        {
            log("InitializeApp: CTK_APP_LOAD_MODEL_PATH='(null)'", CreoLogLevel.Info);
        }

        return ResolveFailure(required, failed ? -1 : 0);
    }

    public static int RunAppActions(
        CreoAppHost appHost,
        bool required,
        Action<string, CreoLogLevel> log)
    {
        ThrowUtil.IfNull(appHost);
        ThrowUtil.IfNull(log);
        var options = DiagnosticsStartupOptions.FromEnvironment();

        if (options.DiagnosticCommand is { } command)
        {
            var rc = appHost.RunCommandForDiagnostics(command);
            log($"InitializeApp: diagnostic command '{command}' rc={rc}.",
                rc == 0 ? CreoLogLevel.Info : CreoLogLevel.Error);
            if (rc != 0)
                return ResolveFailure(required, rc, alwaysFatal: true);
        }

        if (options.RibbonFile is { } ribbonFile)
        {
            var rc = appHost.LoadRibbon(ribbonFile);
            log($"InitializeApp: ribbon load rc={rc} file={ribbonFile}.",
                rc == 0 ? CreoLogLevel.Info : CreoLogLevel.Error);
            if (rc != 0)
                return ResolveFailure(required, rc);
        }

        return 0;
    }

    public static CreoModelType InferModelTypeFromPath(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".prt" => CreoModelType.Part,
            ".asm" => CreoModelType.Assembly,
            ".drw" => CreoModelType.Drawing,
            ".lay" => CreoModelType.Layout,
            ".frm" => CreoModelType.Format,
            ".dgm" => CreoModelType.Diagram,
            ".mrk" => CreoModelType.Markup,
            ".nbk" => CreoModelType.Notebook,
            _ => CreoModelType.Unknown,
        };

    internal static int ResolveFailure(bool required, int failureCode, bool alwaysFatal = false)
        => failureCode == 0 || (!required && !alwaysFatal) ? 0 : failureCode;
}
