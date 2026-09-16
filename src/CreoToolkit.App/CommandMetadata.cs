using CreoToolkit.App.Catalog;

namespace CreoToolkit.App;

/// <summary>
/// 命令元数据强类型（单一真源）。注册时一处声明全部属性，
/// 由生成器派生 txt / 运行时构建 dialog 分组。
/// </summary>
public sealed class CommandMetadata
{
    /// <summary>命令名（ProCmdActionAdd action_name，须唯一，ASCII &lt;= 31 字符）。</summary>
    public required string Name { get; init; }

    /// <summary>按钮标签的 msg key（ASCII）。</summary>
    public required string LabelKey { get; init; }

    /// <summary>帮助文本的 msg key；默认 <see cref="LabelKey"/> + "_HELP"。</summary>
    public string? HelpKey { get; init; }

    /// <summary>按钮标签显示文本（迁入后非 null；迁入前 null 表示文本仍在 txt 手写段）。</summary>
    public string? Label { get; init; }

    /// <summary>帮助显示文本（迁入后非 null；迁入前 null 表示文本仍在 txt 手写段）。</summary>
    public string? Help { get; init; }

    /// <summary>命令语义分类。</summary>
    public CommandKind Kind { get; init; } = CommandKind.Read;

    /// <summary>命令风险等级。</summary>
    public DangerLevel Danger { get; init; } = DangerLevel.Green;

    /// <summary>所属 dialog key（Shell 对话框分组用；null 表示不入任何 dialog）。</summary>
    public string? Dialog { get; init; }

    /// <summary>dialog 内 tab 名（中文；null 时 dialog 自行决定默认 tab）。</summary>
    public string? Tab { get; init; }

    /// <summary>解析后的 HelpKey：优先显式声明，否则 LabelKey + "_HELP"。</summary>
    public string ResolvedHelpKey => HelpKey ?? (LabelKey + "_HELP");
}
