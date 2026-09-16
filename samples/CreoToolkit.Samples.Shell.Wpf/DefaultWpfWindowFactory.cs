using System;
using System.Collections.Generic;
using System.Windows;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;
using CreoToolkit.Samples.Shell.WinForms;
using CreoToolkit.Samples.WpfShell.Views;

namespace CreoToolkit.Samples.WpfShell;

// 默认工厂: dialogKey → ICommandCatalog + BrowserConfig → ShellCommandBrowserWindow。
// 复用 WinForms shell 的 BrowserConfigs (DialogKey 注册表 + Tab 切分 + 危险等级门控 PreRun)。
public sealed class DefaultWpfWindowFactory : IWpfWindowFactory
{
    private readonly IReadOnlyDictionary<string, ICommandCatalog> _catalogs;
    private readonly ICreoApplication? _app;
    private readonly Action<string>? _runSampleByName;
    private readonly Func<string, int?>? _resolveCommandToken;

    public DefaultWpfWindowFactory(
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

    public Window Create(string dialogKey)
    {
        if (!_catalogs.TryGetValue(dialogKey, out var catalog))
            throw new ArgumentException($"Unknown dialogKey: {dialogKey}", nameof(dialogKey));

        var cfg = BrowserConfigs.GetByKey(dialogKey)
            ?? throw new InvalidOperationException($"No BrowserConfig registered for dialogKey: {dialogKey}");

        return new ShellCommandBrowserWindow(cfg, catalog, _app)
        {
            RunSampleByName = _runSampleByName,
            ResolveCommandToken = _resolveCommandToken,
        };
    }
}
