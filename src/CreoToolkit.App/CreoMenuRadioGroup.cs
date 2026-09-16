namespace CreoToolkit.App;

/// <summary>radio 组单项（msg key + 可选图标）。</summary>
public sealed record CreoRadioItem(
    string Name,
    string LabelKey,
    string HelpKey,
    string? IconName = null);

/// <summary>
/// radio 组菜单项声明（对应 ProMenubarmenuRadiogrpAdd）。
/// 引用的 <paramref name="OptionCommandName"/> 必须已经 <see cref="CreoAppBuilder.OptionCommand"/> 声明。
/// </summary>
public sealed record CreoMenuRadioGroup(
    string ParentMenu,
    string GroupName,
    IReadOnlyList<CreoRadioItem> Items,
    string OptionCommandName,
    string? Description = null,
    string? Neighbor = null,
    bool AddAfter = true);
