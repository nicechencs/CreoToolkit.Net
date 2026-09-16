using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms;

// dialogKey → Form 工厂, 由 WinFormsDialogBridge 调用.
public interface IFormFactory
{
    Form Create(string dialogKey);
}
