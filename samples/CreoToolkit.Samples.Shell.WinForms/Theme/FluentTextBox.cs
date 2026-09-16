using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// 扁平 TextBox: 用外层 Panel 画 1px 边 + focus accent, TextBox 自己 BorderStyle.None。
internal sealed class FluentTextBox : Panel
{
    private readonly TextBox _inner;
    private bool _focused;

    public FluentTextBox()
    {
        BackColor = FluentPalette.CardBackground;
        Padding = new Padding(FluentPalette.SpaceSm, 6, FluentPalette.SpaceSm, 6);
        Height = 32;
        _inner = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            Font = FluentPalette.Body,
            BackColor = FluentPalette.CardBackground,
            ForeColor = FluentPalette.TextPrimary,
        };
        _inner.Enter += (_, _) => { _focused = true; Invalidate(); };
        _inner.Leave += (_, _) => { _focused = false; Invalidate(); };
        _inner.HandleCreated += (_, _) =>
        {
            if (_placeholder.Length > 0)
                SendMessage(_inner.Handle, EmSetCueBanner, (IntPtr)1, _placeholder);
        };
        Controls.Add(_inner);
        DoubleBuffered = true;
    }

    public string PlaceholderText
    {
        get => _placeholder;
        set
        {
            _placeholder = value ?? string.Empty;
            if (_inner.IsHandleCreated)
                SendMessage(_inner.Handle, EmSetCueBanner, (IntPtr)1, _placeholder);
        }
    }

    private string _placeholder = string.Empty;
    private const int EmSetCueBanner = 0x1501;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value ?? string.Empty;
    }

    public TextBox Inner => _inner;

    public event System.EventHandler? InnerTextChanged
    {
        add    => _inner.TextChanged += value;
        remove => _inner.TextChanged -= value;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = ClientRectangle;
        rect.Width -= 1; rect.Height -= 1;
        using var bp = new Pen(_focused ? FluentPalette.Accent : FluentPalette.CardBorder, _focused ? 1.5f : 1f);
        e.Graphics.DrawRectangle(bp, rect);
    }
}
