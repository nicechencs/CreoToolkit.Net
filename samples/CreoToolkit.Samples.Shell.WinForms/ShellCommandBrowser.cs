using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CreoToolkit.App;
using CreoToolkit.App.Catalog;
using CreoToolkit.Samples.Shell.WinForms.Theme;

namespace CreoToolkit.Samples.Shell.WinForms;

// 共享 dialog 骨架: 顶 ModelContextBar + 全局搜索 / 中 TabControl (按 cfg.Tabs 切) /
// 右 Detail+Run / 底 JSONL log tail. 7 主题复用此 sealed class, 差异点全在 BrowserConfig.
public sealed class ShellCommandBrowser : FluentForm
{
    private readonly BrowserConfig _cfg;
    private readonly ICommandCatalog _catalog;
    private readonly ICreoApplication? _app;
    private readonly IReadOnlyList<CommandCatalogEntry> _entries;
    private readonly Dictionary<string, List<CommandCatalogEntry>> _entriesByTab = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FluentDataGridView> _gridByTab = new(StringComparer.Ordinal);

    private Label _lblTitle = null!;
    private Label _lblModel = null!;
    private Label _lblModified = null!;
    private Label _lblSession = null!;
    private FluentTextBox _searchBox = null!;
    private FluentTabControl _tabs = null!;
    private Label _detailTitle = null!;
    private Label _detailToken = null!;
    private Label _detailMeta = null!;
    private Label _detailHelp = null!;
    private FluentButton _btnRun = null!;
    private RichTextBox _logBox = null!;
    private bool _busy;
    private CommandCatalogEntry? _selected;

    // RichTextBox 长跑泄漏: 反复 Run 一段时间后 RTF 行数 unbounded, 超过阈值截掉头部。
    private const int LogMaxLines = 500;

    public Action<string>? RunSampleByName { get; set; }
    public Func<string, int?>? ResolveCommandToken { get; set; }

    public ShellCommandBrowser(BrowserConfig cfg, ICommandCatalog catalog, ICreoApplication? app = null)
    {
        _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _app = app;
        _entries = _catalog.FilterByDialogKey(_cfg.DialogKey);

        foreach (var t in _cfg.Tabs) _entriesByTab[t.Title] = new List<CommandCatalogEntry>();
        foreach (var e in _entries)
        {
            // entry 归到第一个 Predicate 为真的 tab; 都不命中则归首 tab (兜底, 漏配 Catalog 时易见)
            var tab = _cfg.Tabs.FirstOrDefault(t => t.Predicate(e)) ?? _cfg.Tabs[0];
            _entriesByTab[tab.Title].Add(e);
        }

        Text = _cfg.FormTitle;
        Size = new Size(1180, 780);
        // MinimumSize 抬到 1024x720: 600 高度下 Detail Card Help 区会被压成负高度,
        // root row 固定占 316px + Form padding 32 + 标题栏 32 = 380px, 高 600 时 Detail 仅剩 220px,
        // 而 Detail 内固定占 228px (title+token+meta+btnRun), Help Fill 区高 -40px 完全消失。
        MinimumSize = new Size(1024, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(FluentPalette.SpaceLg);

        BuildLayout();
        PopulateTabs();
        RefreshContextBar();
    }

    private void BuildLayout()
    {
        // ===== root grid: top context | search | content (split: grid/detail) | log =====
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            BackColor = FluentPalette.WindowBackground,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));

        root.Controls.Add(BuildContextCard(),  0, 0);
        root.Controls.Add(BuildSearchRow(),    0, 1);
        root.Controls.Add(BuildContentSplit(), 0, 2);
        root.Controls.Add(BuildLogCard(),      0, 3);

        Controls.Add(root);
    }

    private FluentCard BuildContextCard()
    {
        var card = new FluentCard
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, FluentPalette.SpaceSm),
            Padding = new Padding(FluentPalette.SpaceLg, FluentPalette.SpaceSm, FluentPalette.SpaceLg, FluentPalette.SpaceSm),
        };

        _lblTitle = new Label
        {
            AutoSize = true,
            Text = _cfg.FormTitle,
            Font = FluentPalette.Subtitle,
            ForeColor = FluentPalette.TextPrimary,
            Margin = new Padding(0, 4, 0, 0),
        };
        var lblSub = new Label
        {
            AutoSize = true,
            Text = "Pro/Toolkit 命令面板 · 选择左侧表格中的条目执行",
            Font = FluentPalette.Caption,
            ForeColor = FluentPalette.TextSecondary,
            Margin = new Padding(0, 2, 0, 0),
        };

        _lblModel    = MakeChip("", "Model: -",       FluentPalette.Accent);
        _lblModified = MakeChip("", "IsModified: -",  FluentPalette.Warning);
        _lblSession  = MakeChip("", "Session: -",     FluentPalette.Success);

        // 双列 TableLayoutPanel: 左 titleStack Fill, 右 chips AutoSize Anchor=Right|Top。
        // 解决原 Location 绝对定位 + chipsPanel.Dock=Right 混用在窄窗口下重叠的问题,
        // chips 靠上留出固定安全边距, 避免在高 DPI/字体替换时被 context card 裁切。
        var titleStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        titleStack.Controls.Add(_lblTitle);
        titleStack.Controls.Add(lblSub);

        var chips = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Right | AnchorStyles.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 10, 0, 0),
        };
        chips.Controls.Add(_lblModel);
        chips.Controls.Add(_lblModified);
        chips.Controls.Add(_lblSession);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(titleStack, 0, 0);
        table.Controls.Add(chips,      1, 0);

        card.Controls.Add(table);
        return card;
    }

    // 状态 "chip": icon + 文字, 单 Label 模拟胶囊。
    private static Label MakeChip(string glyph, string text, Color accent)
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(FluentPalette.SpaceSm, 0, 0, 0),
            Padding = new Padding(FluentPalette.SpaceSm, 4, FluentPalette.SpaceMd, 4),
            BackColor = Color.FromArgb(0x1A, accent.R, accent.G, accent.B),
            ForeColor = FluentPalette.TextPrimary,
            Font = FluentPalette.Caption,
            Text = string.IsNullOrWhiteSpace(glyph) ? text : glyph + "  " + text,
        };
    }

    private Control BuildSearchRow()
    {
        _searchBox = new FluentTextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "    全局过滤 (token / module / labelKey)...",
        };
        _searchBox.InnerTextChanged += (_, _) => ApplyFilter();
        var wrap = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, FluentPalette.SpaceXs, 0, FluentPalette.SpaceSm),
            BackColor = FluentPalette.WindowBackground,
        };
        wrap.Controls.Add(_searchBox);
        return wrap;
    }

    private Control BuildContentSplit()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = FluentPalette.WindowBackground,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));

        var listCard = new FluentCard
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, FluentPalette.SpaceSm, FluentPalette.SpaceSm),
            Padding = new Padding(0),
        };
        _tabs = new FluentTabControl { Dock = DockStyle.Fill };
        foreach (var t in _cfg.Tabs)
        {
            var page = new TabPage(t.Title) { Name = "tab_" + t.Title, BackColor = FluentPalette.CardBackground };
            var dg = BuildGrid();
            dg.Dock = DockStyle.Fill;
            page.Controls.Add(dg);
            _tabs.TabPages.Add(page);
            _gridByTab[t.Title] = dg;
        }
        _tabs.SelectedIndexChanged += (_, _) => UpdateDetailFromActiveGrid();
        listCard.Controls.Add(_tabs);

        grid.Controls.Add(listCard,        0, 0);
        grid.Controls.Add(BuildDetailCard(),1, 0);
        return grid;
    }

    private FluentCard BuildDetailCard()
    {
        var card = new FluentCard
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, FluentPalette.SpaceSm),
            Padding = new Padding(FluentPalette.SpaceLg),
        };

        _detailTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            Text = "命令详情",
            Font = FluentPalette.Subtitle,
            ForeColor = FluentPalette.TextPrimary,
        };
        _detailToken = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "在左侧选一条命令",
            Font = FluentPalette.Mono,
            ForeColor = FluentPalette.TextSecondary,
        };
        _detailMeta = new Label
        {
            Dock = DockStyle.Top,
            // AutoSize 让 meta 高度跟随 4 行字段实际渲染高度 (~88px), 不再写死 130 浪费 42px 给 Help。
            AutoSize = true,
            Text = "",
            Font = FluentPalette.Body,
            ForeColor = FluentPalette.TextPrimary,
            Padding = new Padding(0, FluentPalette.SpaceSm, 0, FluentPalette.SpaceSm),
        };
        _detailHelp = new Label
        {
            Dock = DockStyle.Fill,
            Text = "",
            Font = FluentPalette.Caption,
            ForeColor = FluentPalette.TextSecondary,
        };

        _btnRun = new FluentButton
        {
            Kind = FluentButtonKind.Primary,
            Dock = DockStyle.Bottom,
            Height = 40,
            Text = "  Run",
            Enabled = false,
        };
        _btnRun.Click += (_, _) => InvokeSelected();

        // 加入顺序逆序 (Dock=Bottom 先加在底部)
        card.Controls.Add(_detailHelp);
        card.Controls.Add(_detailMeta);
        card.Controls.Add(_detailToken);
        card.Controls.Add(_detailTitle);
        card.Controls.Add(_btnRun);
        return card;
    }

    private FluentCard BuildLogCard()
    {
        var card = new FluentCard
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(FluentPalette.SpaceMd),
        };
        var hd = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Text = "  实时日志 tail",
            Font = FluentPalette.BodyBold,
            ForeColor = FluentPalette.TextSecondary,
        };
        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = FluentPalette.Mono,
            BackColor = Color.FromArgb(0xFA, 0xFA, 0xFA),
            ForeColor = FluentPalette.TextPrimary,
            BorderStyle = BorderStyle.None,
            WordWrap = false,
        };
        card.Controls.Add(_logBox);
        card.Controls.Add(hd);
        return card;
    }

    private static FluentDataGridView BuildGrid()
    {
        var grid = new FluentDataGridView();
        // 色带列固定 12px (AutoSizeMode=None 直接吃 Width=12, 不参与 Fill 模式分配)。
        // 注意: FillWeight 必须 > 0 (set_FillWeight(0) 抛 ArgumentOutOfRangeException), 即便 AutoSizeMode=None 不参与计算也要给 1。
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Danger", HeaderText = "",       Width = 12, FillWeight = 1,  Resizable = DataGridViewTriState.False, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Token",  HeaderText = "令牌",     FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Module", HeaderText = "模块",     FillWeight = 15 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Label",  HeaderText = "标签键",   FillWeight = 30 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Last",   HeaderText = "上次运行", FillWeight = 18 });
        grid.Columns["Token"]!.DefaultCellStyle.Font = FluentPalette.Mono;
        grid.CellPainting += PaintDangerStripe;
        return grid;
    }

    // Danger 列改用左侧色带替代字体红色, 视觉更安静、辨识更高。
    private static void PaintDangerStripe(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
        var grid = (DataGridView)sender!;
        if (grid.Rows[e.RowIndex].Tag is not CommandCatalogEntry entry) return;

        var color = entry.Danger switch
        {
            DangerLevel.Red    => FluentPalette.Danger,
            DangerLevel.Yellow => FluentPalette.Warning,
            _                  => FluentPalette.Success,
        };
        e.PaintBackground(e.CellBounds, true);
        // 色带宽 3px 居中: X 落点 = 列中心 - 1.5。
        const int stripeWidth = 3;
        int x = e.CellBounds.X + (e.CellBounds.Width - stripeWidth) / 2;
        var stripe = new Rectangle(x, e.CellBounds.Y + 4, stripeWidth, e.CellBounds.Height - 8);
        using var br = new SolidBrush(color);
        e.Graphics!.FillRectangle(br, stripe);
        e.Handled = true;
    }

    private void PopulateTabs()
    {
        foreach (var t in _cfg.Tabs)
        {
            var grid = _gridByTab[t.Title];
            grid.Rows.Clear();
            foreach (var e in _entriesByTab[t.Title])
            {
                var row = grid.Rows.Add("", e.Token, e.Module, e.LabelKey, "");
                grid.Rows[row].Tag = e;
            }
            grid.SelectionChanged += (_, _) => UpdateDetailFromActiveGrid();
            grid.CellDoubleClick += (_, ea) => { if (ea.RowIndex >= 0) { SelectRow(grid, ea.RowIndex); InvokeSelected(); } };
        }
    }

    private void ApplyFilter()
    {
        var q = _searchBox.Text?.Trim() ?? "";
        foreach (var t in _cfg.Tabs)
        {
            var grid = _gridByTab[t.Title];
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Tag is not CommandCatalogEntry e) continue;
                row.Visible = q.Length == 0
                    || e.Token.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || e.Module.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || e.LabelKey.Contains(q, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void SelectRow(DataGridView grid, int rowIndex)
    {
        grid.ClearSelection();
        grid.Rows[rowIndex].Selected = true;
        _selected = grid.Rows[rowIndex].Tag as CommandCatalogEntry;
    }

    private void UpdateDetailFromActiveGrid()
    {
        if (_tabs.SelectedTab is null) return;
        var grid = _gridByTab[_tabs.SelectedTab.Text];
        if (grid.SelectedRows.Count == 0)
        {
            _selected = null;
            _detailToken.Text = "在左侧选一条命令";
            _detailMeta.Text = "";
            _detailHelp.Text = "";
            _btnRun.Enabled = false;
            return;
        }

        _selected = grid.SelectedRows[0].Tag as CommandCatalogEntry;
        if (_selected is null) return;

        var tokenNum = ResolveCommandToken?.Invoke(_selected.Token);
        _detailToken.Text = _selected.Token + (tokenNum is null ? "" : $"   (#{tokenNum})");
        _detailMeta.Text =
            $"Module    {_selected.Module}{Environment.NewLine}" +
            $"Kind      {_selected.Kind}{Environment.NewLine}" +
            $"Danger    {_selected.Danger}{Environment.NewLine}" +
            $"LabelKey  {_selected.LabelKey}";
        _detailHelp.Text = "Help: " + _selected.HelpKey;
        _btnRun.Enabled = !_busy;
    }

    private void InvokeSelected()
    {
        if (_busy || _selected is null) return;
        var entry = _selected;

        // 二次确认 hook (危险等级门控,红=不可逆命令); 不通过则记审计、不写 start, 直接返回.
        if (_cfg.PreRun is not null && !_cfg.PreRun(entry, this))
        {
            CreoAppLog.Info("dialog 命令二次确认未通过", @event: "ui.dialog.command.audit",
                module: _cfg.LogModule,
                props: new { dialogKey = _cfg.DialogKey, commandToken = entry.Token, outcome = "denied" });
            AppendLog($"[audit] {entry.Token} 二次确认未通过");
            return;
        }

        _busy = true;
        _btnRun.Enabled = false;
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
            _btnRun.Enabled = _selected is not null;
        }
    }

    private void RefreshContextBar()
    {
        // app == null 时显示 placeholder; 接 Creo session 后可填模型名 / IsModified 真值.
        _lblModel.Text    = "Model: (待 Creo session)";
        _lblModified.Text = "IsModified: -";
        _lblSession.Text  = _app is null ? "Session: 脱 Creo" : "Session: -";
    }

    private void AppendLog(string line)
    {
        if (_logBox.IsDisposed) return;
        ColorizeAndAppend(line);
        TrimLogIfNeeded();
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void TrimLogIfNeeded()
    {
        if (_logBox.Lines.Length <= LogMaxLines) return;
        var lines = _logBox.Lines;
        var start = Math.Max(0, lines.Length - LogMaxLines);
        var keep = string.Join(Environment.NewLine, lines.Skip(start));
        _logBox.Clear();
        _logBox.AppendText(keep + Environment.NewLine);
    }

    private void ColorizeAndAppend(string line)
    {
        // [run]/[ok]/[err]/[audit] 标签前缀染色, 其余按文字色。
        Color tag = line switch
        {
            _ when line.StartsWith("[err]")   => FluentPalette.Danger,
            _ when line.StartsWith("[audit]") => FluentPalette.Warning,
            _ when line.StartsWith("[ok]")    => FluentPalette.Success,
            _ when line.StartsWith("[run]")   => FluentPalette.Accent,
            _ => FluentPalette.TextSecondary,
        };
        int bracketEnd = line.IndexOf(']') + 1;
        if (bracketEnd > 0)
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionColor = tag;
            _logBox.AppendText(line.Substring(0, bracketEnd));
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionColor = FluentPalette.TextPrimary;
            _logBox.AppendText(line.Substring(bracketEnd) + Environment.NewLine);
        }
        else
        {
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.SelectionColor = FluentPalette.TextPrimary;
            _logBox.AppendText(line + Environment.NewLine);
        }
    }
}
