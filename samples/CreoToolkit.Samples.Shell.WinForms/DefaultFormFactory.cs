using System;
using System.Collections.Generic;
using System.Windows.Forms;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;

namespace CreoToolkit.Samples.Shell.WinForms;

// 默认工厂: dialogKey → ICommandCatalog + BrowserConfig → ShellCommandBrowser.
// 多 dialog 共享同一 bridge 与 run/resolve 回调.
public sealed class DefaultFormFactory : IFormFactory
{
    private readonly IReadOnlyDictionary<string, ICommandCatalog> _catalogs;
    private readonly ICreoApplication? _app;
    private readonly Action<string>? _runSampleByName;
    private readonly Func<string, int?>? _resolveCommandToken;

    public DefaultFormFactory(
        IReadOnlyDictionary<string, ICommandCatalog> catalogs,
        ICreoApplication? app = null,
        Action<string>? runSampleByName = null,
        Func<string, int?>? resolveCommandToken = null)
    {
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
        _app = app;
        _runSampleByName = runSampleByName;
        _resolveCommandToken = resolveCommandToken;
    }

    public Form Create(string dialogKey)
    {
        if (!_catalogs.TryGetValue(dialogKey, out var catalog))
            throw new ArgumentException($"Unknown dialogKey: {dialogKey}", nameof(dialogKey));

        var cfg = BrowserConfigs.GetByKey(dialogKey)
            ?? throw new InvalidOperationException($"No BrowserConfig registered for dialogKey: {dialogKey}");

        return new ShellCommandBrowser(cfg, catalog, _app)
        {
            RunSampleByName = _runSampleByName,
            ResolveCommandToken = _resolveCommandToken,
        };
    }
}
