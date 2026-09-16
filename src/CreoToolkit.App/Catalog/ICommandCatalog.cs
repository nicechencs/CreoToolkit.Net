namespace CreoToolkit.App.Catalog;

// 命令元数据目录：dialog 据此渲染按钮、按归属父菜单分组。
public interface ICommandCatalog
{
    IReadOnlyList<CommandCatalogEntry> All { get; }

    IReadOnlyList<CommandCatalogEntry> FilterByDialogKey(string dialogKey);
}
