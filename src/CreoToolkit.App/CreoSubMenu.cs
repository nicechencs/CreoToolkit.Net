namespace CreoToolkit.App;

/// <summary>
/// 子菜单声明（嵌套层级，对应 ProMenubarmenuMenuAdd）。
/// parent 必须是已 <see cref="CreoAppBuilder.MenuAdd"/> 的菜单或允许的系统菜单或更上层 SubMenu。
/// </summary>
/// <param name="ParentMenu">父菜单内部名（ASCII char[31]）。</param>
/// <param name="MenuName">子菜单内部名（ASCII char[31]，挂按钮时作 parent_menu）。</param>
/// <param name="MenuLabel">子菜单标签 msg key（ASCII char[80]）。</param>
/// <param name="Neighbor">邻居菜单名；null 加在末尾。</param>
/// <param name="AddAfter">true=在 Neighbor 后，false=在前。</param>
public sealed record CreoSubMenu(
    string ParentMenu,
    string MenuName,
    string MenuLabel,
    string? Neighbor = null,
    bool AddAfter = true);
