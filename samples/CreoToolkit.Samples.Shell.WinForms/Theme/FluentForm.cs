using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// Fluent Form 基类: 统一 BackColor / Font / DPI / 标题栏深浅。
// 注意: 本类不启 Mica/Acrylic — WinForms 上做 Mica 要 backdrop type=2 + 透明 BackColor
// + 扩客户区, 复杂且 Win10 无 fallback。仅设 immersive dark mode = Light, 标题栏跟随系统主题。
// WPF 端 (FluentWindow + WindowBackdropType=Mica) 才是真 Mica, 视觉故意有差异作"经典 vs Fluent" 对比。
public class FluentForm : Form
{
    public FluentForm()
    {
        BackColor = FluentPalette.WindowBackground;
        Font = FluentPalette.Body;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyLightTitleBar();
    }

    // DWMWA_USE_IMMERSIVE_DARK_MODE=20, 强制 Light 主题标题栏。
    // Win11 22H2+ 生效; Win10 / Win11 21H2 静默失败不影响功能。
    private void ApplyLightTitleBar()
    {
        try
        {
            int useDark = 0;
            _ = DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
}
