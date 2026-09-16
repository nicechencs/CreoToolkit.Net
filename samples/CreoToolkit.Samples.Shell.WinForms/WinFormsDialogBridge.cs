using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using CreoToolkit.App;
using CreoToolkit.Interop.Dialogs;

namespace CreoToolkit.Samples.Shell.WinForms;

// dialogKey → Form 单例字典. FormClosed 自动移除. 重入 Show 切换到 ActivateExisting.
public sealed class WinFormsDialogBridge : IDialogBridge, IDisposable
{
    private readonly IFormFactory _factory;
    private readonly ConcurrentDictionary<string, Form> _forms = new(StringComparer.Ordinal);
    private readonly object _lifecycleGate = new();
    private int _ownerThreadId;
    private bool _disposed;

    public WinFormsDialogBridge(IFormFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public void Show(string dialogKey, IntPtr? ownerHwnd)
    {
        Form? form = null;
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            ClaimOwnerThread();
            if (_forms.TryGetValue(dialogKey, out var existing) && !existing.IsDisposed)
            {
                ActivateExistingCore(existing);
                return;
            }

            form = _factory.Create(dialogKey);
            form.FormClosed += (_, _) => _forms.TryRemove(dialogKey, out _);
            _forms[dialogKey] = form;
        }

        try
        {
            CreoAppLog.Info("打开 dialog", @event: "ui.dialog.open", module: "shell.winforms",
                props: new { dialogKey });

            if (ownerHwnd is { } hwnd && hwnd != IntPtr.Zero)
                form!.Show(new Win32WindowOwner(hwnd));
            else
                form!.Show();
        }
        catch
        {
            _forms.TryRemove(dialogKey, out _);
            form!.Dispose();
            throw;
        }
    }

    public void Close(string dialogKey)
    {
        if (_disposed) return;
        EnsureOwnerThread();
        if (_forms.TryGetValue(dialogKey, out var form) && !form.IsDisposed)
        {
            SafeInfo("关闭 dialog", "ui.dialog.close", dialogKey);
            try
            {
                form.Close();
            }
            finally
            {
                // A FormClosing handler may cancel Close(). Bridge Close is terminal, so do
                // not leave an undisposed modeless Form alive in that case.
                try { form.Dispose(); }
                catch (Exception ex) { SafeWarn("释放 dialog 失败", dialogKey, ex); }
                _forms.TryRemove(dialogKey, out _);
            }
        }
    }

    public bool IsOpen(string dialogKey)
        => _forms.TryGetValue(dialogKey, out var f) && !f.IsDisposed;

    public void ActivateExisting(string dialogKey)
    {
        ThrowIfDisposed();
        EnsureOwnerThread();
        if (_forms.TryGetValue(dialogKey, out var form) && !form.IsDisposed)
            ActivateExistingCore(form);
    }

    /// <summary>应用终止时关闭仍由 bridge 持有的无模式窗口。</summary>
    public void Dispose()
    {
        KeyValuePair<string, Form>[] forms;
        lock (_lifecycleGate)
        {
            if (_disposed) return;
            EnsureOwnerThread();
            _disposed = true;
            forms = _forms.ToArray();
        }

        foreach (var pair in forms)
            CloseAndDispose(pair.Key, pair.Value);
        _forms.Clear();
    }

    private void CloseAndDispose(string dialogKey, Form form)
    {
        try
        {
            if (!form.IsDisposed)
                form.Close();
        }
        catch (Exception ex)
        {
            SafeWarn("关闭 dialog 失败", dialogKey, ex);
        }
        finally
        {
            try { form.Dispose(); }
            catch (Exception ex) { SafeWarn("释放 dialog 失败", dialogKey, ex); }
        }
    }

    private void ClaimOwnerThread()
    {
        var current = Thread.CurrentThread.ManagedThreadId;
        if (_ownerThreadId == 0)
        {
            _ownerThreadId = current;
            return;
        }
        if (_ownerThreadId != current)
            throw new InvalidOperationException("WinFormsDialogBridge 必须在创建窗口的线程调用。");
    }

    private void EnsureOwnerThread()
    {
        if (_ownerThreadId != 0 && _ownerThreadId != Thread.CurrentThread.ManagedThreadId)
            throw new InvalidOperationException("WinFormsDialogBridge.Dispose/Close 不支持跨线程阻塞调用。");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WinFormsDialogBridge));
    }

    private static void ActivateExistingCore(Form form)
    {
        if (form.WindowState == FormWindowState.Minimized)
            form.WindowState = FormWindowState.Normal;
        form.Activate();
        form.BringToFront();
    }

    private static void SafeWarn(string message, string dialogKey, Exception ex)
    {
        try
        {
            CreoAppLog.Warn(message, @event: "ui.dialog.close-failed", module: "shell.winforms",
                props: new { dialogKey, error = ex.Message });
        }
        catch { /* cleanup must continue when diagnostics are unavailable */ }
    }

    private static void SafeInfo(string message, string eventName, string dialogKey)
    {
        try { CreoAppLog.Info(message, @event: eventName, module: "shell.winforms", props: new { dialogKey }); }
        catch { /* cleanup must continue when diagnostics are unavailable */ }
    }

    // HWND 包成 IWin32Window 让 Show(owner) 能绑 Creo 主窗口.
    private sealed class Win32WindowOwner : IWin32Window
    {
        public Win32WindowOwner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }
}
