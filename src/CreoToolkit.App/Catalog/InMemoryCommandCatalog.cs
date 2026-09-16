namespace CreoToolkit.App.Catalog;

// 进程内命令目录：构造时一次性快照传入条目，过滤走 LINQ。
public sealed class InMemoryCommandCatalog : ICommandCatalog
{
    private readonly List<CommandCatalogEntry> _entries;

    public InMemoryCommandCatalog(IEnumerable<CommandCatalogEntry> entries)
    {
        ThrowUtil.IfNull(entries);
        _entries = new List<CommandCatalogEntry>(entries);
    }

    public IReadOnlyList<CommandCatalogEntry> All => _entries;

    public IReadOnlyList<CommandCatalogEntry> FilterByDialogKey(string dialogKey)
    {
        ThrowUtil.IfNullOrWhiteSpace(dialogKey);
        return _entries
            .Where(e => string.Equals(e.DialogKey, dialogKey, StringComparison.Ordinal))
            .ToList();
    }
}
