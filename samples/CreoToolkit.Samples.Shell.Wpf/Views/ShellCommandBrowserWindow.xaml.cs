using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;
using CreoToolkit.Samples.Shell.WinForms;
using CreoToolkit.Samples.WpfShell.Interop;
using DataGrid = System.Windows.Controls.DataGrid;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Colors = System.Windows.Media.Colors;
using MediaVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace CreoToolkit.Samples.WpfShell.Views;

// 共享 dialog 骨架 (WPF 版): 顶 ContextCard + 全局搜索 / 中 TabControl (按 cfg.Tabs 切) /
// 右 Detail+Run / 底 RichTextBox log tail. 与 WinForms ShellCommandBrowser 功能对等。
public partial class ShellCommandBrowserWindow
{
    private static readonly Color ColAccent  = Color.FromRgb(0x00, 0x78, 0xD4);
    private static readonly Color ColDanger  = Color.FromRgb(0xC4, 0x2B, 0x1C);
    private static readonly Color ColWarning = Color.FromRgb(0xB8, 0x98, 0x00);
    private static readonly Color ColSuccess = Color.FromRgb(0x10, 0x7C, 0x10);
    private static readonly Color ColMuted   = Color.FromRgb(0x5C, 0x5C, 0x5C);

    // RichTextBox 长跑泄漏: 演示用反复 Run 一段时间后 RTF 段落数 unbounded, 超过阈值删头部。
    private const int LogMaxParagraphs = 500;

    private readonly BrowserConfig _cfg;
    private readonly ICommandCatalog _catalog;
    private readonly ICreoApplication? _app;
    private readonly Dictionary<string, List<CommandCatalogEntry>> _entriesByTab = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DataGrid> _gridByTab = new(StringComparer.Ordinal);

    private bool _busy;
    private CommandCatalogEntry? _selected;

    public Action<string>? RunSampleByName { get; set; }
    public Func<string, int?>? ResolveCommandToken { get; set; }

    public ShellCommandBrowserWindow(BrowserConfig cfg, ICommandCatalog catalog, ICreoApplication? app = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _app = app;

        InitializeComponent();
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
            Wpf.Ui.Appearance.ApplicationTheme.Light,
            Wpf.Ui.Controls.WindowBackdropType.Mica);

        Title = _cfg.FormTitle;
        LblTitle.Text = _cfg.FormTitle;

        var entries = _catalog.FilterByDialogKey(_cfg.DialogKey);
        foreach (var t in _cfg.Tabs) _entriesByTab[t.Title] = new List<CommandCatalogEntry>();
        foreach (var e in entries)
        {
            var tab = _cfg.Tabs.FirstOrDefault(t => t.Predicate(e)) ?? _cfg.Tabs[0];
            _entriesByTab[tab.Title].Add(e);
        }

        BuildTabs();
        RefreshContextBar();
        SearchBox.TextChanged += (_, _) => ApplyFilter();
    }

    private void BuildTabs()
    {
        foreach (var t in _cfg.Tabs)
        {
            var dg = BuildGrid();
            foreach (var e in _entriesByTab[t.Title])
                dg.Items.Add(MakeRow(e));
            dg.SelectionChanged += (_, _) => UpdateDetail(dg);
            dg.MouseDoubleClick += (_, args) =>
            {
                // 只在双击落在真实 DataGridRow 时触发, 避免 Header / 空白也触发 InvokeSelected。
                if (args.OriginalSource is DependencyObject src
                    && FindAncestor<DataGridRow>(src) is not null
                    && dg.SelectedItem is RowVm)
                    InvokeSelected();
            };

            _gridByTab[t.Title] = dg;
            TabsCtl.Items.Add(new TabItem { Header = t.Title, Content = dg });
        }
        TabsCtl.SelectionChanged += (_, e) =>
        {
            if (e.Source is not System.Windows.Controls.TabControl) return;
            if (TabsCtl.SelectedItem is TabItem ti && ti.Content is DataGrid dg) UpdateDetail(dg);
        };
    }

    private static DataGrid BuildGrid()
    {
        var dg = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserResizeRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            BorderThickness = new Thickness(0),
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            IsReadOnly = true,
            RowHeight = 36,
            FontSize = 13,
        };
        dg.Columns.Add(new DataGridTemplateColumn { Header = "", Width = new DataGridLength(12), CellTemplate = MakeStripeCell() });
        dg.Columns.Add(new DataGridTextColumn { Header = "令牌",    Binding = new System.Windows.Data.Binding(nameof(RowVm.Token)),    Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "模块",    Binding = new System.Windows.Data.Binding(nameof(RowVm.Module)),   Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "标签键",  Binding = new System.Windows.Data.Binding(nameof(RowVm.LabelKey)), Width = new DataGridLength(3, DataGridLengthUnitType.Star) });
        dg.Columns.Add(new DataGridTextColumn { Header = "上次运行", Binding = new System.Windows.Data.Binding(nameof(RowVm.LastRun)),  Width = new DataGridLength(1.8, DataGridLengthUnitType.Star) });
        return dg;
    }

    // 第一列窄色带: 红/黄/绿表示 Danger。
    private static DataTemplate MakeStripeCell()
    {
        var tpl = new DataTemplate(typeof(RowVm));
        var border = new FrameworkElementFactory(typeof(System.Windows.Shapes.Rectangle));
        border.SetValue(System.Windows.Shapes.Rectangle.WidthProperty, 3.0);
        border.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 6, 0, 6));
        border.SetBinding(System.Windows.Shapes.Rectangle.FillProperty, new System.Windows.Data.Binding(nameof(RowVm.StripeBrush)));
        tpl.VisualTree = border;
        return tpl;
    }

    private RowVm MakeRow(CommandCatalogEntry e) => new()
    {
        Token = e.Token,
        Module = e.Module,
        LabelKey = e.LabelKey,
        LastRun = "",
        Entry = e,
        StripeBrush = e.Danger switch
        {
            DangerLevel.Red    => new SolidColorBrush(ColDanger),
            DangerLevel.Yellow => new SolidColorBrush(ColWarning),
            _                  => new SolidColorBrush(ColSuccess),
        },
    };

    private void ApplyFilter()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        foreach (var grid in _gridByTab.Values)
        {
            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.Items);
            if (q.Length == 0)
            {
                view.Filter = null;
                continue;
            }
            view.Filter = o => o is RowVm r &&
                (r.Token.Contains(q, StringComparison.OrdinalIgnoreCase)
                 || r.Module.Contains(q, StringComparison.OrdinalIgnoreCase)
                 || r.LabelKey.Contains(q, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void UpdateDetail(DataGrid grid)
    {
        if (grid.SelectedItem is not RowVm row)
        {
            _selected = null;
            DetailToken.Text = "在左侧选一条命令";
            DetailMeta.Text = "";
            DetailHelp.Text = "";
            BtnRun.IsEnabled = false;
            return;
        }
        _selected = row.Entry;
        var tokenNum = ResolveCommandToken?.Invoke(_selected.Token);
        DetailToken.Text = _selected.Token + (tokenNum is null ? "" : $"   (#{tokenNum})");
        DetailMeta.Text =
            $"Module    {_selected.Module}\n" +
            $"Kind      {_selected.Kind}\n" +
            $"Danger    {_selected.Danger}\n" +
            $"LabelKey  {_selected.LabelKey}";
        DetailHelp.Text = "Help: " + _selected.HelpKey;
        BtnRun.IsEnabled = !_busy;
    }

    private void OnRunClick(object sender, RoutedEventArgs e) => InvokeSelected();

    private void InvokeSelected()
    {
        if (_busy || _selected is null) return;
        var entry = _selected;

        if (_cfg.PreRun is not null && !_cfg.PreRun(entry, Win32WindowOwner.FromWpf(this)))
        {
            CreoAppLog.Info("dialog 命令二次确认未通过", @event: "ui.dialog.command.audit",
                module: _cfg.LogModule,
                props: new { dialogKey = _cfg.DialogKey, commandToken = entry.Token, outcome = "denied" });
            AppendLog($"[audit] {entry.Token} 二次确认未通过");
            return;
        }

        _busy = true;
        BtnRun.IsEnabled = false;
        AppendLog($"[run]   {entry.Token}");
        CreoAppLog.Info("dialog 命令开始", @event: "ui.dialog.command.start",
            module: _cfg.LogModule,
            props: new { dialogKey = _cfg.DialogKey, commandToken = entry.Token, commandName = entry.Token });
        try
        {
            RunSampleByName?.Invoke(entry.Token);
            CreoAppLog.Info("dialog 命令完成", @event: "ui.dialog.command.end",
                module: _cfg.LogModule,
                props: new { dialogKey = _cfg.DialogKey, commandToken = entry.Token });
            AppendLog($"[ok]    {entry.Token}");
        }
        catch (Exception ex)
        {
            CreoAppLog.Error("dialog 命令异常", ex,
                @event: "ui.dialog.command.error",
                module: _cfg.LogModule,
                props: new { dialogKey = _cfg.DialogKey, commandToken = entry.Token });
            AppendLog($"[err]   {entry.Token} : {ex.Message}");
        }
        finally
        {
            _busy = false;
            BtnRun.IsEnabled = _selected is not null;
        }
    }

    private void RefreshContextBar()
    {
        LblModel.Text    = "Model: (待 Creo session)";
        LblModified.Text = "IsModified: -";
        LblSession.Text  = _app is null ? "Session: 脱 Creo" : "Session: -";
    }

    private void AppendLog(string line)
    {
        var doc = LogBox.Document;
        var p = new Paragraph { Margin = new Thickness(0) };
        var tagColor = line switch
        {
            _ when line.StartsWith("[err]")   => ColDanger,
            _ when line.StartsWith("[audit]") => ColWarning,
            _ when line.StartsWith("[ok]")    => ColSuccess,
            _ when line.StartsWith("[run]")   => ColAccent,
            _ => ColMuted,
        };
        var bracketEnd = line.IndexOf(']') + 1;
        if (bracketEnd > 0)
        {
            p.Inlines.Add(new Run(line.Substring(0, bracketEnd)) { Foreground = new SolidColorBrush(tagColor) });
            p.Inlines.Add(new Run(line.Substring(bracketEnd)));
        }
        else
        {
            p.Inlines.Add(new Run(line));
        }
        doc.Blocks.Add(p);
        while (doc.Blocks.Count > LogMaxParagraphs && doc.Blocks.FirstBlock is { } first)
            doc.Blocks.Remove(first);
        LogBox.ScrollToEnd();
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = MediaVisualTreeHelper.GetParent(node);
        }
        return null;
    }

    // WPF binding 反射 binder 在 .NET 8 下访问 private nested type 会静默失败 (PresentationTraceSources warning),
    // 实测左侧色带 binding 不显示。改 internal 即可被 PresentationFramework 反射。
    internal sealed class RowVm
    {
        public string Token { get; init; } = "";
        public string Module { get; init; } = "";
        public string LabelKey { get; init; } = "";
        public string LastRun { get; set; } = "";
        public CommandCatalogEntry Entry { get; init; } = null!;
        public SolidColorBrush StripeBrush { get; init; } = new(Colors.Transparent);
    }
}
