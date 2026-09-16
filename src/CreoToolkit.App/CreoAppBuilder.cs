namespace CreoToolkit.App;

/// <summary>
/// 声明式命令注册器。<see cref="Command"/> 顺序分配 token、拒绝重名;
/// <see cref="MenuAdd"/> 声明自定义顶级菜单,<see cref="MenuButton"/> 声明菜单按钮。
/// <para>
/// **ExtCreoMenu 扩展面**：<see cref="SubMenu"/>（子菜单嵌套）/ <see cref="OptionCommand"/>
/// （ProCmdOptionAdd 注册 radio/check 用命令）/ <see cref="MenuCheckButton"/> / <see cref="MenuRadioGroup"/>
/// / <see cref="PopupNotification"/> / <see cref="LoadCustomRibbon"/>。所有新声明在 <see cref="BuildDefinition"/>
/// 内随原有 Commands/Menus/MenuButtons 一同打包，<see cref="CreoAppHost.Run"/> 顺序消费。
/// </para>
/// </summary>
public sealed class CreoAppBuilder
{
    private const int ProNameMax = 31;
    private const int ProLineMax = 80;

    private readonly List<CreoCommand> _cmds = new();
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);
    private readonly List<CreoMenu> _menusToCreate = new();
    private readonly HashSet<string> _menuNames = new(StringComparer.Ordinal);
    private readonly List<string> _extraSystemMenus = new();
    private readonly List<CreoMenuButton> _menus = new();

    // 扩展字段
    private readonly List<CreoSubMenu> _subMenus = new();
    private readonly HashSet<string> _subMenuNames = new(StringComparer.Ordinal);
    private readonly List<CreoOptionCommand> _optionCommands = new();
    private readonly HashSet<string> _optionCommandNames = new(StringComparer.Ordinal);
    private readonly List<CreoMenuCheckButton> _checkButtons = new();
    private readonly List<CreoMenuRadioGroup> _radioGroups = new();
    private readonly List<CreoPopupNotification> _popupNotifications = new();
    private readonly HashSet<CreoPopupNotificationType> _popupNotificationTypes = new();
    private readonly List<CreoCustomRibbon> _customRibbons = new();

    /// <summary>声明一条命令。label/help 为 msg key。重名抛 <see cref="InvalidOperationException"/>。</summary>
    public CreoAppBuilder Command(string name, string labelKey, string helpKey, CreoCommandHandler handler)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        ThrowUtil.IfNull(handler);
        ValidateAsciiMax(labelKey ?? "", ProLineMax, nameof(labelKey));
        ValidateAsciiMax(helpKey ?? "", ProLineMax, nameof(helpKey));

        if (!_names.Add(name))
            throw new InvalidOperationException($"命令名重复: {name}");

        _cmds.Add(new CreoCommand
        {
            Token = _cmds.Count,
            Name = name,
            LabelKey = labelKey ?? "",
            HelpKey = helpKey ?? "",
            Handler = handler,
        });
        return this;
    }

    /// <summary>声明一条命令（元数据驱动）。HelpKey 默认 LabelKey+"_HELP"。</summary>
    public CreoAppBuilder Command(CommandMetadata metadata, CreoCommandHandler handler)
    {
        ThrowUtil.IfNull(metadata);
        ThrowUtil.IfNull(handler);

        var helpKey = metadata.ResolvedHelpKey;
        ValidateAsciiMax(metadata.LabelKey, ProLineMax, nameof(metadata.LabelKey));
        ValidateAsciiMax(helpKey, ProLineMax, "HelpKey");

        if (!_names.Add(metadata.Name))
            throw new InvalidOperationException($"命令名重复: {metadata.Name}");

        _cmds.Add(new CreoCommand
        {
            Token = _cmds.Count,
            Name = metadata.Name,
            LabelKey = metadata.LabelKey,
            HelpKey = helpKey,
            Handler = handler,
            Metadata = metadata,
        });
        return this;
    }

    /// <summary>声明一条顶级菜单,host 会在挂按钮前创建它。</summary>
    public CreoAppBuilder MenuAdd(string menuName, string menuLabel,
        string? neighbor = null, bool addAfter = true)
    {
        ThrowUtil.IfNullOrWhiteSpace(menuName);
        ThrowUtil.IfNullOrWhiteSpace(menuLabel);
        ValidateAsciiMax(menuName, ProNameMax, nameof(menuName));
        ValidateAsciiMax(menuLabel, ProLineMax, nameof(menuLabel));
        ValidateAsciiMax(neighbor, ProNameMax, nameof(neighbor));

        if (!_menuNames.Add(menuName))
            throw new InvalidOperationException($"菜单名重复: {menuName}");

        _menusToCreate.Add(new CreoMenu(menuName, menuLabel, neighbor, addAfter));
        return this;
    }

    /// <summary>补充当前应用允许直挂的系统菜单名。</summary>
    public CreoAppBuilder AddSystemMenu(string menuName)
    {
        ThrowUtil.IfNullOrWhiteSpace(menuName);
        ValidateAsciiMax(menuName, ProNameMax, nameof(menuName));
        _extraSystemMenus.Add(menuName);
        return this;
    }

    /// <summary>
    /// 声明一条菜单按钮:把已声明的命令挂到 <paramref name="parentMenu"/>/<paramref name="buttonName"/> 位置。
    /// commandName 必须是已经 Command(...) 声明过的命令名(由 <see cref="BuildDefinition"/> 校验)。
    /// </summary>
    public CreoAppBuilder MenuButton(string parentMenu, string buttonName,
        string commandName, string? neighbor = null, bool addAfter = true)
    {
        ThrowUtil.IfNullOrWhiteSpace(parentMenu);
        ThrowUtil.IfNullOrWhiteSpace(buttonName);
        ThrowUtil.IfNullOrWhiteSpace(commandName);
        ValidateAsciiMax(parentMenu, ProNameMax, nameof(parentMenu));
        ValidateAsciiMax(buttonName, ProNameMax, nameof(buttonName));
        ValidateAsciiMax(neighbor, ProNameMax, nameof(neighbor));

        _menus.Add(new CreoMenuButton(parentMenu, buttonName, commandName, neighbor, addAfter));
        return this;
    }

    // ========== ExtCreoMenu 扩展面 ==========

    /// <summary>声明一条子菜单（嵌套层级），parent 必须是已 MenuAdd 的菜单或允许的系统菜单。</summary>
    public CreoAppBuilder SubMenu(string parentMenu, string menuName, string menuLabel,
        string? neighbor = null, bool addAfter = true)
    {
        ThrowUtil.IfNullOrWhiteSpace(parentMenu);
        ThrowUtil.IfNullOrWhiteSpace(menuName);
        ThrowUtil.IfNullOrWhiteSpace(menuLabel);
        ValidateAsciiMax(parentMenu, ProNameMax, nameof(parentMenu));
        ValidateAsciiMax(menuName, ProNameMax, nameof(menuName));
        ValidateAsciiMax(menuLabel, ProLineMax, nameof(menuLabel));
        ValidateAsciiMax(neighbor, ProNameMax, nameof(neighbor));

        if (!_subMenuNames.Add(menuName))
            throw new InvalidOperationException($"子菜单名重复: {menuName}");

        _subMenus.Add(new CreoSubMenu(parentMenu, menuName, menuLabel, neighbor, addAfter));
        return this;
    }

    /// <summary>
    /// 声明 option command（radio/check 后端）。actHandler/valHandler 走纯 .NET callback hub。
    /// option command 名与普通 Command 共享命名空间（底层都是 ProCmdActionAdd/OptionAdd），不可重名。
    /// </summary>
    public CreoAppBuilder OptionCommand(string name, string labelKey, string helpKey,
        bool defaultBoolean,
        CreoOptionActionHandler actHandler,
        CreoOptionValueHandler valHandler)
    {
        ThrowUtil.IfNullOrWhiteSpace(name);
        ThrowUtil.IfNull(actHandler);
        ThrowUtil.IfNull(valHandler);
        ValidateAsciiMax(labelKey ?? "", ProLineMax, nameof(labelKey));
        ValidateAsciiMax(helpKey ?? "", ProLineMax, nameof(helpKey));

        if (!_names.Add(name) || !_optionCommandNames.Add(name))
            throw new InvalidOperationException($"option command 名重复或与普通命令冲突: {name}");

        _optionCommands.Add(new CreoOptionCommand(name, labelKey ?? "", helpKey ?? "",
            defaultBoolean, actHandler, valHandler));
        return this;
    }

    /// <summary>声明 check 菜单项；optionCommandName 必须已经 OptionCommand(...) 声明。</summary>
    public CreoAppBuilder MenuCheckButton(string parentMenu, string buttonName,
        string optionCommandName, string labelKey, string? helpKey = null,
        string? neighbor = null, bool addAfter = true)
    {
        ThrowUtil.IfNullOrWhiteSpace(parentMenu);
        ThrowUtil.IfNullOrWhiteSpace(buttonName);
        ThrowUtil.IfNullOrWhiteSpace(optionCommandName);
        ValidateAsciiMax(parentMenu, ProNameMax, nameof(parentMenu));
        ValidateAsciiMax(buttonName, ProNameMax, nameof(buttonName));
        ValidateAsciiMax(labelKey ?? "", ProLineMax, nameof(labelKey));
        ValidateAsciiMax(neighbor, ProNameMax, nameof(neighbor));

        _checkButtons.Add(new CreoMenuCheckButton(parentMenu, buttonName, optionCommandName,
            labelKey ?? "", helpKey, neighbor, addAfter));
        return this;
    }

    /// <summary>声明 radio 组菜单项；optionCommandName 必须已经 OptionCommand(...) 声明，items 非空。</summary>
    public CreoAppBuilder MenuRadioGroup(string parentMenu, string groupName,
        IReadOnlyList<CreoRadioItem> items, string optionCommandName,
        string? description = null, string? neighbor = null, bool addAfter = true)
    {
        ThrowUtil.IfNullOrWhiteSpace(parentMenu);
        ThrowUtil.IfNullOrWhiteSpace(groupName);
        ThrowUtil.IfNullOrWhiteSpace(optionCommandName);
        ThrowUtil.IfNull(items);
        if (items.Count == 0) throw new ArgumentException("radio items cannot be empty", nameof(items));
        ValidateAsciiMax(parentMenu, ProNameMax, nameof(parentMenu));
        ValidateAsciiMax(groupName, ProNameMax, nameof(groupName));
        ValidateAsciiMax(neighbor, ProNameMax, nameof(neighbor));

        _radioGroups.Add(new CreoMenuRadioGroup(parentMenu, groupName, items, optionCommandName,
            description, neighbor, addAfter));
        return this;
    }

    /// <summary>声明 popup notification handler；notifyType 见 Pro/Toolkit pro_notify_type enum。</summary>
    public CreoAppBuilder PopupNotification(
        CreoPopupNotificationType notifyType,
        CreoPopupNotificationHandler handler)
    {
        ThrowUtil.IfNull(handler);
        if (!_popupNotificationTypes.Add(notifyType))
            throw new InvalidOperationException($"popup notification type 重复: {notifyType}");
        _popupNotifications.Add(new CreoPopupNotification(notifyType, handler));
        return this;
    }

    [Obsolete("使用 CreoPopupNotificationType typed overload；未知值使用 CreoPopupNotificationType.FromRaw。")]
    public CreoAppBuilder PopupNotification(int notifyType, CreoPopupNotificationHandler handler)
        => PopupNotification(CreoPopupNotificationType.FromRaw(notifyType), handler);

    /// <summary>声明加载自定义 ribbon 文件（.rbn）。</summary>
    public CreoAppBuilder LoadCustomRibbon(string filePath)
    {
        ThrowUtil.IfNullOrWhiteSpace(filePath);
        _customRibbons.Add(new CreoCustomRibbon(filePath));
        return this;
    }

    /// <summary>取应用声明完整快照(Commands + Menus + MenuButtons + 扩展面),并校验引用完整性。</summary>
    public CreoAppDefinition BuildDefinition()
    {
        var missing = _menus
            .Where(m => !_names.Contains(m.CommandName))
            .Select(m => m.CommandName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException($"菜单引用了未声明命令: {string.Join(", ", missing)}");

        // check/radio 必须引用已声明的 option command
        var missingOptions = _checkButtons.Select(c => c.OptionCommandName)
            .Concat(_radioGroups.Select(r => r.OptionCommandName))
            .Where(n => !_optionCommandNames.Contains(n))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missingOptions.Length > 0)
            throw new InvalidOperationException($"check/radio 引用了未声明 option command: {string.Join(", ", missingOptions)}");

        var validParents = new HashSet<string>(SystemMenuNames.BuildAllowList(_extraSystemMenus), StringComparer.Ordinal);
        validParents.UnionWith(_menuNames);
        // sub-menu 也算合法 parent（嵌套层级）
        validParents.UnionWith(_subMenuNames);

        var badParent = _menus
            .Where(m => !validParents.Contains(m.ParentMenu))
            .Select(m => $"{m.ButtonName}->{m.ParentMenu}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (badParent.Length > 0)
        {
            throw new InvalidOperationException(
                $"菜单按钮 parentMenu 未声明也非系统菜单: {string.Join(", ", badParent)}。" +
                $"已声明 MenuAdd: {string.Join("/", _menuNames)};" +
                $"已声明 SubMenu: {string.Join("/", _subMenuNames)};" +
                $"系统菜单: {string.Join("/", SystemMenuNames.Builtin)};" +
                "或先调 app.MenuAdd(...) / app.SubMenu(...) 声明 / app.AddSystemMenu(...) / 设 CTK_APP_SYSMENU_EXTRA env。");
        }

        // check/radio/sub-menu 的 parentMenu 也要在 validParents 中
        var badExtraParent = _checkButtons.Select(c => $"check:{c.ButtonName}->{c.ParentMenu}")
            .Concat(_radioGroups.Select(r => $"radio:{r.GroupName}->{r.ParentMenu}"))
            .Concat(_subMenus.Select(s => $"sub:{s.MenuName}->{s.ParentMenu}"))
            .Where(s => !validParents.Contains(s.Substring(s.IndexOf("->") + 2)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (badExtraParent.Length > 0)
            throw new InvalidOperationException($"扩展项 parentMenu 未声明: {string.Join(", ", badExtraParent)}");

        return new CreoAppDefinition(_cmds.ToArray(), _menusToCreate.ToArray(), _menus.ToArray())
        {
            SubMenus = _subMenus.ToArray(),
            OptionCommands = _optionCommands.ToArray(),
            MenuCheckButtons = _checkButtons.ToArray(),
            MenuRadioGroups = _radioGroups.ToArray(),
            PopupNotifications = _popupNotifications.ToArray(),
            CustomRibbons = _customRibbons.ToArray(),
        };
    }

    /// <summary>取命令快照(不可变副本)。等价于 BuildDefinition().Commands;旧 API 兼容。</summary>
    public IReadOnlyList<CreoCommand> Build() => BuildDefinition().Commands;

    /// <summary>后附元数据：按命令名查找并附加 Metadata（不覆盖已有的）。</summary>
    public CreoAppBuilder AttachMetadata(string commandName, CommandMetadata metadata)
    {
        ThrowUtil.IfNullOrWhiteSpace(commandName);
        ThrowUtil.IfNull(metadata);

        var cmd = _cmds.FirstOrDefault(c => c.Name == commandName);
        if (cmd is null)
            throw new InvalidOperationException($"命令未注册: {commandName}");
        if (cmd.Metadata is null)
            cmd.Metadata = metadata;
        return this;
    }

    private static void ValidateAsciiMax(string? value, int maxChars, string paramName)
    {
        if (value is null)
            return;

        if (value.Length > maxChars || value.Any(c => c > 0x7f))
            throw new ArgumentException($"{paramName} must be <= {maxChars} ASCII chars", paramName);
    }
}
