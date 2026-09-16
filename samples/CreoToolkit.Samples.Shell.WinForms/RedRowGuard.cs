using System;
using System.Drawing;
using System.Windows.Forms;
using CreoToolkit.App.Catalog;
using CreoToolkit.Samples.Shell.WinForms.Theme;

namespace CreoToolkit.Samples.Shell.WinForms;

// 危险等级门控(红=不可逆命令)二次确认: Green/Yellow 直接放行, Red 弹 modal checkbox + Execute.
// BrowserConfig.PreRunHook 注入此 helper, 让 7 主题 dialog 共享同一条危险等级门控.
internal static class RedRowGuard
{
    // 暴露给单测: 判断是否需要弹 modal 二次确认.
    public static bool ShouldConfirm(DangerLevel d) => d == DangerLevel.Red;

    public static bool ConfirmIfRed(CommandCatalogEntry entry, IWin32Window owner)
    {
        if (!ShouldConfirm(entry.Danger)) return true;
        using var dlg = new RedRowConfirmDialog(entry);
        return dlg.ShowDialog(owner) == DialogResult.OK;
    }
}

// 不可逆命令的 modal 确认对话框. checkbox 勾选才使 Execute 可点.
internal sealed class RedRowConfirmDialog : FluentForm
{
    public RedRowConfirmDialog(CommandCatalogEntry entry)
    {
        ThrowUtil.IfNull(entry);

        Text = "破坏性命令二次确认";
        Size = new Size(640, 320);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Padding = new Padding(FluentPalette.SpaceXl);

        // 顶部 Danger banner: 红色色带 + 标题。
        var banner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = FluentPalette.DangerSubtle,
            Padding = new Padding(FluentPalette.SpaceLg, FluentPalette.SpaceMd, FluentPalette.SpaceLg, FluentPalette.SpaceMd),
        };
        var bannerStripe = new Panel
        {
            Dock = DockStyle.Left,
            Width = 4,
            BackColor = FluentPalette.Danger,
        };
        var lblBar = new Label
        {
            Dock = DockStyle.Fill,
            Text = "  破坏性命令 · 写入模型且不可回退",
            ForeColor = FluentPalette.Danger,
            Font = FluentPalette.Subtitle,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        banner.Controls.Add(lblBar);
        banner.Controls.Add(bannerStripe);

        // 信息区: 字段表 + Help 说明。
        var info = new Label
        {
            Dock = DockStyle.Top,
            Height = 110,
            Padding = new Padding(FluentPalette.SpaceLg, FluentPalette.SpaceMd, FluentPalette.SpaceLg, 0),
            Text = $"Token    {entry.Token}{Environment.NewLine}" +
                   $"Module   {entry.Module}{Environment.NewLine}" +
                   $"Kind     {entry.Kind}{Environment.NewLine}" +
                   $"Label    {entry.LabelKey}",
            Font = FluentPalette.Mono,
            ForeColor = FluentPalette.TextPrimary,
        };

        var btnOK = new FluentButton
        {
            Kind = FluentButtonKind.Danger,
            Text = "执行",
            Width = 120,
            Height = 36,
            Enabled = false,
            DialogResult = DialogResult.OK,
            Margin = new Padding(FluentPalette.SpaceSm, 0, 0, 0),
        };
        var btnCancel = new FluentButton
        {
            Kind = FluentButtonKind.Default,
            Text = "取消",
            Width = 120,
            Height = 36,
            DialogResult = DialogResult.Cancel,
        };

        var chk = new CheckBox
        {
            Dock = DockStyle.Top,
            Text = "  我已理解并确认执行此破坏性操作",
            Font = FluentPalette.Body,
            ForeColor = FluentPalette.TextPrimary,
            Padding = new Padding(FluentPalette.SpaceLg, FluentPalette.SpaceSm, FluentPalette.SpaceLg, FluentPalette.SpaceSm),
            AutoSize = false,
            Height = 40,
            FlatStyle = FlatStyle.Flat,
        };
        chk.CheckedChanged += (_, _) => btnOK.Enabled = chk.Checked;

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, FluentPalette.SpaceSm, FluentPalette.SpaceLg, FluentPalette.SpaceSm),
            BackColor = FluentPalette.WindowBackground,
        };
        btnPanel.Controls.Add(btnOK);
        btnPanel.Controls.Add(btnCancel);

        AcceptButton = btnOK;
        CancelButton = btnCancel;

        Controls.Add(btnPanel);
        Controls.Add(chk);
        Controls.Add(info);
        Controls.Add(banner);
    }
}
