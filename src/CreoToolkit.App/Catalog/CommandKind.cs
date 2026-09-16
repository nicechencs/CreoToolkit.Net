namespace CreoToolkit.App.Catalog;

// 命令语义分类，用于 dialog 行级 Tag 显示与 Red 行二次确认门控。
public enum CommandKind
{
    Read,
    Roundtrip,
    Mutation,
    Diagnostics,
    Demo,
}
