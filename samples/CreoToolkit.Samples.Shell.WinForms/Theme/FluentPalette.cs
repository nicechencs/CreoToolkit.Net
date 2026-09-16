using System.Drawing;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// Win11 Fluent Light 主题调色板。颜色/字体/间距/圆角统一常量。
// 主题切换 (Dark) 只改本文件值, 控件无需改。
internal static class FluentPalette
{
    // ---------- 表层 ----------
    public static readonly Color WindowBackground = Color.FromArgb(0xF3, 0xF3, 0xF3);
    public static readonly Color CardBackground   = Color.White;
    public static readonly Color CardBorder       = Color.FromArgb(0xE5, 0xE5, 0xE5);
    public static readonly Color Divider          = Color.FromArgb(0xEB, 0xEB, 0xEB);

    // ---------- 强调色 ----------
    public static readonly Color Accent           = Color.FromArgb(0x00, 0x78, 0xD4);
    public static readonly Color AccentHover      = Color.FromArgb(0x10, 0x6E, 0xBE);
    public static readonly Color AccentPressed    = Color.FromArgb(0x00, 0x5A, 0x9E);
    public static readonly Color AccentSubtle     = Color.FromArgb(0x33, 0x00, 0x78, 0xD4);

    // ---------- 文字 ----------
    public static readonly Color TextPrimary      = Color.FromArgb(0x1F, 0x1F, 0x1F);
    public static readonly Color TextSecondary    = Color.FromArgb(0x5C, 0x5C, 0x5C);
    public static readonly Color TextTertiary     = Color.FromArgb(0xA0, 0xA0, 0xA0);
    public static readonly Color TextOnAccent     = Color.White;

    // ---------- 语义色 (Danger/Warning/Success) ----------
    public static readonly Color Danger           = Color.FromArgb(0xC4, 0x2B, 0x1C);
    public static readonly Color DangerHover      = Color.FromArgb(0xB8, 0x2A, 0x1B);
    public static readonly Color DangerSubtle     = Color.FromArgb(0x14, 0xC4, 0x2B, 0x1C);
    public static readonly Color Warning          = Color.FromArgb(0xB8, 0x98, 0x00);
    public static readonly Color WarningSubtle    = Color.FromArgb(0x18, 0xB8, 0x98, 0x00);
    public static readonly Color Success          = Color.FromArgb(0x10, 0x7C, 0x10);

    // ---------- 控件态 ----------
    public static readonly Color HoverOverlay     = Color.FromArgb(0x14, 0x00, 0x00, 0x00); // 8% black
    public static readonly Color PressedOverlay   = Color.FromArgb(0x28, 0x00, 0x00, 0x00); // 16% black
    public static readonly Color RowSelected      = Color.FromArgb(0x33, 0x00, 0x78, 0xD4);
    public static readonly Color RowHover         = Color.FromArgb(0x14, 0x00, 0x78, 0xD4);

    // ---------- 间距 / 圆角 ----------
    public const int SpaceXs = 4;
    public const int SpaceSm = 8;
    public const int SpaceMd = 12;
    public const int SpaceLg = 16;
    public const int SpaceXl = 24;
    public const int Radius  = 6;
    public const int RadiusSm = 4;

    // ---------- 字体 ----------
    // 用 lazy field 单例: 每次 get 都 new Font 会让 caller (Control.Font / CellStyle.Font) 拿到匿名 GDI handle
    // 而 Control / CellStyle 都不接管 ownership, 反复打开 dialog 会单调泄漏 GDI handle 直到系统拒分配。
    private static Font? _body, _bodyBold, _caption, _subtitle, _mono;
    public static Font Body     => _body     ??= CreatePreferred("Segoe UI Variable Text",    "Segoe UI",          9.5f, FontStyle.Regular);
    public static Font BodyBold => _bodyBold ??= CreatePreferred("Segoe UI Variable Text",    "Segoe UI",          9.5f, FontStyle.Bold);
    public static Font Caption  => _caption  ??= CreatePreferred("Segoe UI Variable Small",   "Segoe UI",          8.5f, FontStyle.Regular);
    public static Font Subtitle => _subtitle ??= CreatePreferred("Segoe UI Variable Display", "Segoe UI Semibold", 11f,  FontStyle.Bold);
    public static Font Mono     => _mono     ??= CreatePreferred("Cascadia Mono",             "Consolas",          9f,   FontStyle.Regular);

    // 优先 family1, 缺失回退 family2 (Segoe UI Variable 在 Win11 才有, Win10 退 Segoe UI)。
    private static Font CreatePreferred(string preferred, string fallback, float size, FontStyle style)
    {
        try
        {
            var f = new Font(preferred, size, style, GraphicsUnit.Point);
            if (string.Equals(f.Name, preferred, System.StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        catch
        {
            // 跌到 fallback
        }
        return new Font(fallback, size, style, GraphicsUnit.Point);
    }
}
