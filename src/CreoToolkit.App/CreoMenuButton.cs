namespace CreoToolkit.App;

/// <summary>
/// 菜单按钮声明:把已声明的命令挂到传统菜单栏的某个位置。
/// 由 <see cref="CreoAppBuilder.MenuButton"/> 收集到 <see cref="CreoAppDefinition.MenuButtons"/>,
/// host 在 designate 完所有命令后、菜单按钮挂载前先按声明顺序创建 <see cref="CreoMenu"/>,
/// 再经 <see cref="Interop.ICommandBridge.MenubarPushbuttonAdd"/> 挂按钮(治本)。
/// </summary>
/// <param name="ParentMenu">父菜单名:必须是同 app 已 <see cref="CreoAppBuilder.MenuAdd"/> 声明的菜单,
/// 或 <see cref="SystemMenuNames"/> 系统菜单白名单成员(如 "File"/"Help"/"Info"),
/// 或经 <see cref="CreoAppBuilder.AddSystemMenu"/> / CTK_APP_SYSMENU_EXTRA env 扩充。
/// BuildDefinition 阶段校验存在；lint 编译期早失败。</param>
/// <param name="ButtonName">本按钮在父菜单下的内部 name。</param>
/// <param name="CommandName">绑定的命令名(由 <see cref="CreoAppBuilder.Command"/> 声明);Build 阶段会校验存在。</param>
/// <param name="Neighbor">邻居按钮 name,可空。null 时本按钮放置在父菜单尾部。</param>
/// <param name="AddAfter">true=插在 Neighbor 之后,false=之前;Neighbor 为空时此参数被忽略。</param>
public sealed record CreoMenuButton(
    string ParentMenu,
    string ButtonName,
    string CommandName,
    string? Neighbor = null,
    bool AddAfter = true);
