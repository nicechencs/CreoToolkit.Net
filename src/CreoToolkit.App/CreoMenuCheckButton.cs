namespace CreoToolkit.App;

/// <summary>
/// check 菜单项声明（对应 ProMenubarmenuChkbuttonAdd）。
/// 引用的 <paramref name="OptionCommandName"/> 必须已经 <see cref="CreoAppBuilder.OptionCommand"/> 声明。
/// </summary>
public sealed record CreoMenuCheckButton(
    string ParentMenu,
    string ButtonName,
    string OptionCommandName,
    string LabelKey,
    string? HelpKey = null,
    string? Neighbor = null,
    bool AddAfter = true);
