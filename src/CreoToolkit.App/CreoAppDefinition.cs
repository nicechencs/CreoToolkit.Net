namespace CreoToolkit.App;

/// <summary>
/// 应用声明快照:命令、需创建的顶级菜单、菜单按钮，以及 ExtCreoMenu 扩展面（子菜单 / option command /
/// check / radio / popup notification / 自定义 ribbon）。由 <see cref="CreoAppBuilder.BuildDefinition"/> 返回,
/// <see cref="CreoAppHost.Run"/> 消费。<see cref="CreoAppBuilder.Build"/> 保留为旧 API 兼容。
/// </summary>
/// <param name="Commands">所有已声明命令,顺序与 token 分配顺序一致。</param>
/// <param name="Menus">所有需创建的自定义顶级菜单,顺序与 MenuAdd 调用顺序一致。</param>
/// <param name="MenuButtons">所有菜单按钮声明,顺序与 MenuButton 调用顺序一致。</param>
public sealed record CreoAppDefinition(
    IReadOnlyList<CreoCommand> Commands,
    IReadOnlyList<CreoMenu> Menus,
    IReadOnlyList<CreoMenuButton> MenuButtons)
{
    /// <summary>子菜单（嵌套层级）；按声明顺序消费。</summary>
    public IReadOnlyList<CreoSubMenu> SubMenus { get; init; } = Array.Empty<CreoSubMenu>();

    /// <summary>option command 声明（radio/check 用 ProCmdOptionAdd 注册）；按声明顺序消费。</summary>
    public IReadOnlyList<CreoOptionCommand> OptionCommands { get; init; } = Array.Empty<CreoOptionCommand>();

    /// <summary>check 菜单项声明（绑定到 option command）。</summary>
    public IReadOnlyList<CreoMenuCheckButton> MenuCheckButtons { get; init; } = Array.Empty<CreoMenuCheckButton>();

    /// <summary>radio 组菜单项声明（绑定到 option command）。</summary>
    public IReadOnlyList<CreoMenuRadioGroup> MenuRadioGroups { get; init; } = Array.Empty<CreoMenuRadioGroup>();

    /// <summary>popup notification 声明（ProNotificationSet）；Host.Dispose 反序反注册（加强项 5）。</summary>
    public IReadOnlyList<CreoPopupNotification> PopupNotifications { get; init; } = Array.Empty<CreoPopupNotification>();

    /// <summary>自定义 ribbon 文件加载声明。</summary>
    public IReadOnlyList<CreoCustomRibbon> CustomRibbons { get; init; } = Array.Empty<CreoCustomRibbon>();
}
