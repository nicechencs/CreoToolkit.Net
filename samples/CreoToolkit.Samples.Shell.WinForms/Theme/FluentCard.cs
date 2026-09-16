using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// 卡片容器: 白底 + 1px Mica grey border + 圆角. 用于 ContextBar / Detail / Log 三段分组。
internal sealed class FluentCard : Panel
{
    public FluentCard()
    {
        BackColor = FluentPalette.CardBackground;
        Padding = new Padding(FluentPalette.SpaceMd);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        rect.Width -= 1; rect.Height -= 1;
        using var path = RoundedRect(rect, FluentPalette.Radius);
        using var bg = new SolidBrush(BackColor);
        g.FillPath(bg, path);
        using var border = new Pen(FluentPalette.CardBorder, 1f);
        g.DrawPath(border, path);
    }

    internal static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
