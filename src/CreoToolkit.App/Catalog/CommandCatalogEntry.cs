namespace CreoToolkit.App.Catalog;

// 命令元数据快照。token 是命令 name 字符串；token 数值由 builder 在 build 后另算。
public sealed class CommandCatalogEntry
{
    public string Token { get; }
    public string Module { get; }
    public CommandKind Kind { get; }
    public DangerLevel Danger { get; }
    public string LabelKey { get; }
    public string HelpKey { get; }
    public string DialogKey { get; }

    public CommandCatalogEntry(
        string token,
        string module,
        CommandKind kind,
        DangerLevel danger,
        string labelKey,
        string helpKey,
        string dialogKey)
    {
        ThrowUtil.IfNullOrWhiteSpace(token);
        ThrowUtil.IfNullOrWhiteSpace(module);
        ThrowUtil.IfNull(labelKey);
        ThrowUtil.IfNull(helpKey);
        ThrowUtil.IfNullOrWhiteSpace(dialogKey);

        Token = token;
        Module = module;
        Kind = kind;
        Danger = danger;
        LabelKey = labelKey;
        HelpKey = helpKey;
        DialogKey = dialogKey;
    }
}
