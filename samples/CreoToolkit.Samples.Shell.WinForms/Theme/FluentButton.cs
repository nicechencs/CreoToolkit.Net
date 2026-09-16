using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

internal enum FluentButtonKind { Default, Primary, Subtle, Danger }

// 自绘按钮: 4 种语义色 + hover/pressed/disabled 4 态。圆角 RadiusSm。
internal sealed class FluentButton : Button
{
    private bool _hover;
    private bool _pressed;
    private FluentButtonKind _kind = FluentButtonKind.Default;

    public FluentButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        Font = FluentPalette.Body;
        Height = 32;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        ApplyKindColors();
    }

    public FluentButtonKind Kind
    {
        get => _kind;
        set { _kind = value; ApplyKindColors(); Invalidate(); }
    }

    private void ApplyKindColors()
    {
        ForeColor = _kind switch
        {
            FluentButtonKind.Primary => FluentPalette.TextOnAccent,
            FluentButtonKind.Danger  => FluentPalette.TextOnAccent,
            _                        => FluentPalette.TextPrimary,
        };
    }

    protected override void OnMouseEnter(System.EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(System.EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)   { _pressed = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var rect = ClientRectangle;
        rect.Width -= 1; rect.Height -= 1;
        using var path = FluentCard.RoundedRect(rect, FluentPalette.RadiusSm);

        var (fill, border) = ResolveColors();
        using var bg = new SolidBrush(fill);
        g.FillPath(bg, path);
        if (border != Color.Transparent)
        {
            using var bp = new Pen(border, 1f);
            g.DrawPath(bp, path);
        }

        TextRenderer.DrawText(g, Text ?? "", Font, rect,
            Enabled ? ForeColor : FluentPalette.TextTertiary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private (Color Fill, Color Border) ResolveColors()
    {
        if (!Enabled)
        {
            return _kind switch
            {
                FluentButtonKind.Primary or FluentButtonKind.Danger
                    => (FluentPalette.CardBorder, Color.Transparent),
                _   => (Color.Transparent, FluentPalette.CardBorder),
            };
        }
        return _kind switch
        {
            FluentButtonKind.Primary => (
                _pressed ? FluentPalette.AccentPressed : _hover ? FluentPalette.AccentHover : FluentPalette.Accent,
                Color.Transparent),
            FluentButtonKind.Danger => (
                _pressed ? FluentPalette.AccentPressed : _hover ? FluentPalette.DangerHover : FluentPalette.Danger,
                Color.Transparent),
            FluentButtonKind.Subtle => (
                _pressed ? FluentPalette.PressedOverlay : _hover ? FluentPalette.HoverOverlay : Color.Transparent,
                Color.Transparent),
            _ => (
                _pressed ? FluentPalette.PressedOverlay : _hover ? FluentPalette.HoverOverlay : FluentPalette.CardBackground,
                FluentPalette.CardBorder),
        };
    }
}
