using System;
using System.Windows;
using System.Windows.Interop;

namespace CreoToolkit.Samples.WpfShell.Interop;

// 把 WPF Window 包成 IWin32Window, 让 WinForms 模态 (RedRowConfirmDialog) 能用 WPF 作 owner。
internal sealed class Win32WindowOwner : System.Windows.Forms.IWin32Window
{
    public Win32WindowOwner(IntPtr handle) => Handle = handle;
    public IntPtr Handle { get; }

    public static Win32WindowOwner FromWpf(Window w)
    {
        // Window 未 Show 时 Handle 默认 IntPtr.Zero, EnsureHandle 强制创建 HWND
        // 避免下游 ShowDialog(Zero) modal 失去 owner、Alt-Tab 顺序错乱。
        var helper = new WindowInteropHelper(w);
        helper.EnsureHandle();
        return new Win32WindowOwner(helper.Handle);
    }
}
