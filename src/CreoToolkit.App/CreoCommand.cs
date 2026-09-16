namespace CreoToolkit.App;

/// <summary>命令点击回调：业务只看到 <see cref="CreoCommandContext"/>，看不到 native 边界。</summary>
public delegate void CreoCommandHandler(CreoCommandContext ctx);

/// <summary>
/// 一条已声明的命令。<see cref="Token"/> 由 builder 顺序分配（贯穿 native 路由）。
/// <see cref="LabelKey"/>/<see cref="HelpKey"/> 是 msg 文件里的 key（非 UI 文本，F9）。
/// </summary>
public sealed class CreoCommand
{
    /// <summary>路由 token（builder 顺序分配，0 起）。</summary>
    public int Token { get; init; }

    /// <summary>命令名（ProCmdActionAdd 的 action_name，须唯一）。</summary>
    public string Name { get; init; } = "";

    /// <summary>按钮标签的 msg key。</summary>
    public string LabelKey { get; init; } = "";

    /// <summary>一行帮助的 msg key。</summary>
    public string HelpKey { get; init; } = "";

    /// <summary>点击回调。</summary>
    public CreoCommandHandler Handler { get; init; } = _ => { };

    /// <summary>强类型元数据（新 overload 注册时附带；或 post-registration 附加）。</summary>
    public CommandMetadata? Metadata { get; set; }
}
