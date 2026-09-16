namespace CreoToolkit.Interop.Dialogs;

// dialogKey 用 string 而非 enum：sample 模块可外部扩展，避免 Interop 跟着改枚举。
public interface IDialogBridge
{
    void Show(string dialogKey, IntPtr? ownerHwnd);
    void Close(string dialogKey);
    bool IsOpen(string dialogKey);
    void ActivateExisting(string dialogKey);
}
