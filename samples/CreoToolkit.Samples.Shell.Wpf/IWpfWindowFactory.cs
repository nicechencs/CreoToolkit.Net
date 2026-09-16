using System.Windows;

namespace CreoToolkit.Samples.WpfShell;

// dialogKey → Window 工厂, 由 WpfDialogBridge 调用。
public interface IWpfWindowFactory
{
    Window Create(string dialogKey);
}
