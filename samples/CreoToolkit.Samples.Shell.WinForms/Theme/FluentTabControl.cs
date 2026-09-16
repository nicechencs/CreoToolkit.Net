using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// 扁平 TabControl: 去 Win32 立体感, 选中下划线 accent。底色与窗口对齐。
internal sealed class FluentTabControl : TabControl
{
    public FluentTabControl()
    {
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(140, 36);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        Appearance = TabAppearance.Normal;
        Font = FluentPalette.Body;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new SolidBrush(FluentPalette.WindowBackground))
            g.FillRectangle(bg, ClientRectangle);

        for (int i = 0; i < TabPages.Count; i++)
        {
            var rect = GetTabRect(i);
            bool selected = SelectedIndex == i;
            var text = TabPages[i].Text;

            if (selected)
            {
                using var bg = new SolidBrush(FluentPalette.CardBackground);
                g.FillRectangle(bg, rect);
                var underline = new Rectangle(rect.X + 12, rect.Bottom - 3, rect.Width - 24, 3);
                using var ac = new SolidBrush(FluentPalette.Accent);
                g.FillRectangle(ac, underline);
            }

            TextRenderer.DrawText(g, text, Font, rect,
                selected ? FluentPalette.TextPrimary : FluentPalette.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // tab 与 page 之间的浅分割线
        int dividerY = ItemSize.Height + 2;
        using var dp = new Pen(FluentPalette.Divider, 1f);
        g.DrawLine(dp, 0, dividerY, ClientRectangle.Width, dividerY);
    }
}
