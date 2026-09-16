using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CreoToolkit.App;
using CreoToolkit.Interop.Dialogs;
using Application = System.Windows.Application;

namespace CreoToolkit.Samples.WpfShell;

// dialogKey → Window 单例字典. Closed 自动移除. 重入 Show 切换到 ActivateExisting.
// 与 WinFormsDialogBridge 平级, 由宿主按 dialogKey 路由。
public sealed class WpfDialogBridge : IDialogBridge, IDisposable
{
    private readonly IWpfWindowFactory _factory;
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);
    private readonly Dispatcher _dispatcher;
    private readonly object _lifecycleGate = new();
    private bool _disposed;

    public WpfDialogBridge(IWpfWindowFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        EnsureApplication();
        _dispatcher = Application.Current!.Dispatcher;
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("WpfDialogBridge must be created on the application Dispatcher/Creo owner thread.");
    }

    public void Show(string dialogKey, IntPtr? ownerHwnd)
    {
        ThrowIfDisposed();
        // Synchronous Toolkit callbacks and dialog commands share the Creo owner thread.
        InvokeOnDispatcher(() => ShowCore(dialogKey, ownerHwnd));
    }

    private void ShowCore(string dialogKey, IntPtr? ownerHwnd)
    {
        ThrowIfDisposed();
        if (TryActivateLive(dialogKey)) return;

        var w = _factory.Create(dialogKey);
        w.Closed += (_, _) => _windows.TryRemove(dialogKey, out _);
        _windows[dialogKey] = w;

        try
        {
            CreoAppLog.Info("打开 dialog", @event: "ui.dialog.open", module: "shell.wpf",
                props: new { dialogKey });

            if (ownerHwnd is { } hwnd && hwnd != IntPtr.Zero)
                new WindowInteropHelper(w).Owner = hwnd;

            w.Show();
        }
        catch
        {
            // A constructed Window is app-owned even if logging/owner/Show fails
            // before it obtains a presentation source.
            CloseTerminal(dialogKey, w);
            throw;
        }
    }

    public void Close(string dialogKey)
    {
        if (_disposed) return;
        InvokeOnDispatcher(() =>
        {
            if (!_windows.TryGetValue(dialogKey, out var w) || !IsLive(w)) return;
            SafeInfo("关闭 dialog", "ui.dialog.close", dialogKey);
            w.Close();
        });
    }

    // IsOpen / ActivateExisting 与 WinFormsDialogBridge 语义对齐:
    // 仅当 Window 已 Loaded 且未 Closed 时算 open; Closed 与字典 remove 之间的窗口期不会误报。
    public bool IsOpen(string dialogKey)
        => _windows.TryGetValue(dialogKey, out var w) && IsLive(w);

    public void ActivateExisting(string dialogKey)
    {
        ThrowIfDisposed();
        InvokeOnDispatcher(() => TryActivateLive(dialogKey));
    }

    /// <summary>
    /// Application termination closes modeless windows on their owning Dispatcher only; it never
    /// performs a cross-thread blocking Invoke. A terminal close installs a final Closing handler
    /// that overrides ordinary cancellation, then verifies the window actually closed.
    /// </summary>
    public void Dispose()
    {
        KeyValuePair<string, Window>[] windows;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            if (!_dispatcher.CheckAccess())
            {
                SafeWarn("关闭 WPF dialogs 时不在 owner Dispatcher", "", new InvalidOperationException(
                    "WpfDialogBridge.Dispose 不支持跨线程阻塞调用。"));
                throw new InvalidOperationException("WpfDialogBridge.Dispose 必须在 WPF Dispatcher owner thread 调用。");
            }
            _disposed = true;
            windows = _windows.ToArray();
        }

        foreach (var pair in windows)
            CloseTerminal(pair.Key, pair.Value);
    }

    private void CloseTerminal(string dialogKey, Window window)
    {
        if (!_windows.TryGetValue(dialogKey, out var owned) || !ReferenceEquals(owned, window))
            return;

        CancelEventHandler forceTerminalClose = (_, e) => e.Cancel = false;
        window.Closing += forceTerminalClose;
        try
        {
            window.Close();
            if (_windows.TryGetValue(dialogKey, out owned) && ReferenceEquals(owned, window))
            {
                // Keep the entry for diagnostics instead of falsely claiming ownership ended.
                SafeWarn("WPF dialog terminal close was canceled or did not complete", dialogKey,
                    new InvalidOperationException("Window remained live after Close()."));
            }
        }
        catch (Exception ex)
        {
            SafeWarn("关闭 dialog 失败", dialogKey, ex);
        }
        finally
        {
            window.Closing -= forceTerminalClose;
        }
    }

    private bool TryActivateLive(string dialogKey)
    {
        if (!_windows.TryGetValue(dialogKey, out var w)) return false;
        if (!IsLive(w))
        {
            _windows.TryRemove(dialogKey, out _);
            return false;
        }
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        return true;
    }

    private static bool IsLive(Window w)
    {
        // IsLoaded=true 且 PresentationSource 仍在; 已 Close 的 Window 这两条都假。
        return w.IsLoaded && PresentationSource.FromVisual(w) is not null;
    }

    // No blocking cross-thread Invoke: Toolkit work must stay on the Creo owner thread.
    private void InvokeOnDispatcher(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }
        throw new InvalidOperationException("WpfDialogBridge operations must run on its owning Dispatcher thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WpfDialogBridge));
    }

    private static void SafeInfo(string message, string eventName, string dialogKey)
    {
        try { CreoAppLog.Info(message, @event: eventName, module: "shell.wpf", props: new { dialogKey }); }
        catch { /* cleanup must continue when diagnostics are unavailable */ }
    }

    private static void SafeWarn(string message, string dialogKey, Exception ex)
    {
        try
        {
            CreoAppLog.Warn(message, @event: "ui.dialog.close-failed", module: "shell.wpf",
                props: new { dialogKey, error = ex.Message });
        }
        catch { /* cleanup must continue when diagnostics are unavailable */ }
    }

    // 宿主非 WPF App (Pro/Toolkit 主线程) 时, Application 单例可能尚未创建。
    // 这里兜底起一个空 Application, 让 WPF 框架的 Dispatcher / Resource / Theme 能正常初始化。
    // 测试场景下同一 AppDomain 内可能已经被别处 new 过, 吞掉异常但二次校验 Current 仍 null 时抛真错。
    private static void EnsureApplication()
    {
        if (Application.Current is not null) return;
        try { _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; }
        catch (InvalidOperationException) { /* 别处已 new, 复用即可 */ }
        if (Application.Current is null)
            throw new InvalidOperationException(
                "WpfDialogBridge: Application.Current 仍为 null, 可能当前线程不是 STA 或 WPF 框架未初始化。");
    }
}
