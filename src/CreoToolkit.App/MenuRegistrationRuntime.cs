using CreoToolkit.App.Errors;
using CreoToolkit.Interop;
using CreoToolkit.Interop.Diagnostics;
using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>把声明式菜单和扩展面翻译到 native bridge；外观项可降级，命令级 option/designate 仍硬失败。</summary>
internal sealed class MenuRegistrationRuntime
{
    private readonly ICommandBridge _bridge;
    private readonly ICommandDispatcher _dispatcher;
    private readonly CreoSession? _session;
    private readonly Func<string?, CreoMessages> _createMessages;
    private readonly string _msgFile;

    internal MenuRegistrationRuntime(
        ICommandBridge bridge,
        ICommandDispatcher dispatcher,
        CreoSession? session,
        Func<string?, CreoMessages> createMessages,
        string msgFile)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _session = session;
        _createMessages = createMessages ?? throw new ArgumentNullException(nameof(createMessages));
        _msgFile = msgFile ?? throw new ArgumentNullException(nameof(msgFile));
    }

    internal void Register(CreoAppDefinition definition, Dictionary<string, nint> commandIds)
    {
        ThrowUtil.IfNull(definition);
        ThrowUtil.IfNull(commandIds);

        var tally = new MenuTally();
        RegisterMenus(definition, tally);

        if (_bridge is IMenuBridge menuBridgeForSubs)
            RegisterSubMenus(definition, menuBridgeForSubs, tally);

        RegisterButtons(definition, commandIds, tally);

        if (_bridge is IMenuBridge menuBridge)
        {
            RegisterExtensions(definition, commandIds, menuBridge, tally);
        }
        else if (definition.SubMenus.Count + definition.OptionCommands.Count +
                 definition.MenuCheckButtons.Count + definition.MenuRadioGroups.Count +
                 definition.PopupNotifications.Count + definition.CustomRibbons.Count > 0)
        {
            CreoLog.Warn("bridge 未实现 IMenuBridge — 扩展面跳过（fake/test 模式）",
                @event: "menu.ext.skip", module: "CreoAppHost", layer: CreoLogLayer.App);
        }

        WriteSummary(tally);
    }

    private void RegisterMenus(CreoAppDefinition definition, MenuTally tally)
    {
        foreach (var menu in definition.Menus)
        {
            var rc = _bridge.MenubarMenuAdd(
                menu.MenuName, menu.MenuLabel, menu.Neighbor, menu.AddAfter, _msgFile);
            if (rc != 0)
            {
                tally.FailedMenus.Add(menu.MenuName);
                tally.Skipped++;
                CreoLog.Warn($"MenubarMenuAdd 失败 menu={menu.MenuName} rc={rc}",
                    @event: "menu.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { menu.MenuName, menu.MenuLabel, menu.Neighbor, menu.AddAfter, rc });
                continue;
            }

            tally.Registered++;
            CreoLog.Info($"MenubarMenuAdd menu={menu.MenuName}",
                @event: "menu.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { menu.MenuName, menu.MenuLabel, rc });
        }
    }

    private void RegisterSubMenus(CreoAppDefinition definition, IMenuBridge menuBridge, MenuTally tally)
    {
        foreach (var sub in definition.SubMenus)
        {
            if (IsParentFailed(tally, sub.ParentMenu, "MenubarmenuMenuAdd", sub.MenuName))
            {
                tally.FailedMenus.Add(sub.MenuName);
                continue;
            }

            var rc = menuBridge.MenubarmenuMenuAdd(
                sub.ParentMenu, sub.MenuName, sub.MenuLabel, sub.Neighbor, sub.AddAfter, _msgFile);
            if (rc != 0)
            {
                tally.FailedMenus.Add(sub.MenuName);
                tally.Skipped++;
                CreoLog.Warn($"MenubarmenuMenuAdd 失败 sub={sub.MenuName} parent={sub.ParentMenu} rc={rc}",
                    @event: "menu.sub.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { sub.ParentMenu, sub.MenuName, sub.MenuLabel, sub.Neighbor, sub.AddAfter, rc });
                continue;
            }

            tally.Registered++;
            CreoLog.Info($"MenubarmenuMenuAdd sub={sub.MenuName}",
                @event: "menu.sub.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { sub.ParentMenu, sub.MenuName, rc });
        }
    }

    private void RegisterButtons(
        CreoAppDefinition definition,
        Dictionary<string, nint> commandIds,
        MenuTally tally)
    {
        foreach (var menu in definition.MenuButtons)
        {
            if (!commandIds.TryGetValue(menu.CommandName, out var commandId))
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarPushbuttonAdd 跳过 button={menu.ButtonName} cmd={menu.CommandName} 未注册",
                    @event: "menu.button.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { menu.ParentMenu, menu.ButtonName, menu.CommandName, reason = "command-not-registered" });
                continue;
            }

            if (IsParentFailed(tally, menu.ParentMenu, "MenubarPushbuttonAdd", menu.ButtonName))
                continue;

            var command = definition.Commands.First(c =>
                string.Equals(c.Name, menu.CommandName, StringComparison.Ordinal));
            var rc = _bridge.MenubarPushbuttonAdd(
                menu.ParentMenu, menu.ButtonName, command.LabelKey, command.HelpKey,
                menu.Neighbor, menu.AddAfter, commandId, _msgFile);
            if (rc != 0)
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarPushbuttonAdd 失败 button={menu.ButtonName} parent={menu.ParentMenu} cmd={menu.CommandName} rc={rc}",
                    @event: "menu.button.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new
                    {
                        menu.ParentMenu,
                        menu.ButtonName,
                        menu.CommandName,
                        menu.Neighbor,
                        menu.AddAfter,
                        rc,
                        reason = "bridge-rc-nonzero",
                    });
                continue;
            }

            tally.Registered++;
            CreoLog.Info($"MenubarPushbuttonAdd button={menu.ButtonName}",
                @event: "menu.button.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { menu.ParentMenu, menu.ButtonName, menu.CommandName, rc });
        }
    }

    private void RegisterExtensions(
        CreoAppDefinition definition,
        Dictionary<string, nint> commandIds,
        IMenuBridge menuBridge,
        MenuTally tally)
    {
        RegisterOptionCommands(definition, commandIds, menuBridge);
        RegisterCheckButtons(definition, commandIds, menuBridge, tally);
        RegisterRadioGroups(definition, commandIds, menuBridge, tally);
        RegisterPopupNotifications(definition, menuBridge);
        RegisterCustomRibbons(definition);
    }

    private void RegisterOptionCommands(
        CreoAppDefinition definition,
        Dictionary<string, nint> commandIds,
        IMenuBridge menuBridge)
    {
        foreach (var option in definition.OptionCommands)
        {
            var current = option;
            OptionCmdActHandler nativeAction = (commandId, value) =>
            {
                try
                {
                    _dispatcher.Invoke(() =>
                    {
                        var context = new CreoOptionActionContext(
                            _session, _createMessages(current.Name), menuBridge, commandId, value);
                        current.ActionHandler(context);
                    });
                }
                catch (Exception ex)
                {
                    CreoLog.Error($"option act handler 异常 cmd={current.Name}",
                        @event: "option.act.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                        err: CreoLog.WithError(ex));
                }
            };
            OptionCmdValHandler nativeValue = (commandId, value) =>
            {
                try
                {
                    current.ValueHandler(new CreoOptionValueContext(menuBridge, commandId, value));
                }
                catch (Exception ex)
                {
                    CreoLog.Error($"option val handler 异常 cmd={current.Name}",
                        @event: "option.val.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                        err: CreoLog.WithError(ex));
                }
            };

            var rc = menuBridge.CommandOptionAdd(
                option.Name, option.DefaultBoolean, nativeAction, nativeValue, out var optionCommandId);
            Check(rc, $"CommandOptionAdd[{option.Name}]");
            commandIds[option.Name] = optionCommandId;
            CreoLog.Info($"OptionCommandAdd cmd={option.Name}",
                @event: "option.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { cmd = option.Name, optCmdId = (long)optionCommandId, rc });
        }
    }

    private void RegisterCheckButtons(
        CreoAppDefinition definition,
        Dictionary<string, nint> commandIds,
        IMenuBridge menuBridge,
        MenuTally tally)
    {
        foreach (var button in definition.MenuCheckButtons)
        {
            if (!commandIds.TryGetValue(button.OptionCommandName, out var commandId))
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarmenuChkbuttonAdd 跳过 btn={button.ButtonName} option={button.OptionCommandName} 未注册",
                    @event: "menu.check.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { button.ParentMenu, button.ButtonName, button.OptionCommandName, reason = "option-not-registered" });
                continue;
            }

            if (IsParentFailed(tally, button.ParentMenu, "MenubarmenuChkbuttonAdd", button.ButtonName))
                continue;

            var rc = menuBridge.MenubarmenuChkbuttonAdd(
                button.ParentMenu, button.ButtonName, button.LabelKey, button.HelpKey,
                button.Neighbor, button.AddAfter, commandId, _msgFile);
            if (rc != 0)
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarmenuChkbuttonAdd 失败 btn={button.ButtonName} parent={button.ParentMenu} rc={rc}",
                    @event: "menu.check.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { button.ParentMenu, button.ButtonName, button.Neighbor, button.AddAfter, rc, reason = "bridge-rc-nonzero" });
                continue;
            }

            tally.Registered++;
            CreoLog.Info($"MenubarmenuChkbuttonAdd btn={button.ButtonName}",
                @event: "menu.check.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { button.ParentMenu, button.ButtonName, rc });
        }
    }

    private void RegisterRadioGroups(
        CreoAppDefinition definition,
        Dictionary<string, nint> commandIds,
        IMenuBridge menuBridge,
        MenuTally tally)
    {
        foreach (var group in definition.MenuRadioGroups)
        {
            if (!commandIds.TryGetValue(group.OptionCommandName, out var commandId))
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarmenuRadiogrpAdd 跳过 grp={group.GroupName} option={group.OptionCommandName} 未注册",
                    @event: "menu.radio.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { group.ParentMenu, group.GroupName, group.OptionCommandName, reason = "option-not-registered" });
                continue;
            }

            if (IsParentFailed(tally, group.ParentMenu, "MenubarmenuRadiogrpAdd", group.GroupName))
                continue;

            var nativeItems = group.Items
                .Select(item => new RadioGroupItem(item.Name, item.LabelKey, item.HelpKey, item.IconName))
                .ToArray();
            var rc = menuBridge.MenubarmenuRadiogrpAdd(
                group.ParentMenu, group.GroupName, nativeItems,
                group.Neighbor, group.AddAfter, commandId, _msgFile);
            if (rc != 0)
            {
                tally.Skipped++;
                CreoLog.Warn($"MenubarmenuRadiogrpAdd 失败 grp={group.GroupName} parent={group.ParentMenu} rc={rc}",
                    @event: "menu.radio.add.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { group.ParentMenu, group.GroupName, count = nativeItems.Length, rc, reason = "bridge-rc-nonzero" });
                continue;
            }

            var designateRc = menuBridge.CommandRadiogrpDesignate(
                commandId, nativeItems, group.Description, _msgFile);
            Check(designateRc, $"CommandRadiogrpDesignate[{group.GroupName}]");
            tally.Registered++;
            CreoLog.Info($"MenubarmenuRadiogrpAdd grp={group.GroupName}",
                @event: "menu.radio.add.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { group.ParentMenu, group.GroupName, count = nativeItems.Length, rc });
        }
    }

    private void RegisterPopupNotifications(CreoAppDefinition definition, IMenuBridge menuBridge)
    {
        foreach (var notification in definition.PopupNotifications)
        {
            var handler = notification.Handler;
            PopupNotifyHandler wrapper = menuName =>
            {
                try
                {
                    _dispatcher.Invoke(() => handler(menuName));
                }
                catch (Exception ex)
                {
                    CreoLog.Error($"popup notify handler 异常 notifyType={notification.NotifyType}",
                        @event: "popup.notify.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                        err: CreoLog.WithError(ex));
                }
            };
            var rc = menuBridge.NotificationSet(notification.NotifyType.Value, wrapper);
            Check(rc, $"NotificationSet[{notification.NotifyType}]");
            CreoLog.Info($"NotificationSet type={notification.NotifyType}",
                @event: "popup.notify.set.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                props: new { notification.NotifyType, rc });
        }
    }

    private void RegisterCustomRibbons(CreoAppDefinition definition)
    {
        foreach (var ribbon in definition.CustomRibbons)
        {
            var rc = _bridge.RibbonDefinitionfileLoad(ribbon.FilePath);
            if (rc != 0)
            {
                CreoLog.Warn($"CustomRibbon 加载失败 file={ribbon.FilePath}",
                    @event: "ribbon.custom.fail", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { ribbon.FilePath, rc });
            }
            else
            {
                CreoLog.Info($"CustomRibbon 加载 file={ribbon.FilePath}",
                    @event: "ribbon.custom.ok", module: "CreoAppHost", layer: CreoLogLayer.App,
                    props: new { ribbon.FilePath, rc });
            }
        }
    }

    private static void WriteSummary(MenuTally tally)
    {
        if (tally.Registered + tally.Skipped == 0)
            return;

        var summary = $"菜单外观项注册完成:成功 {tally.Registered},跳过 {tally.Skipped}";
        var props = new
        {
            registered = tally.Registered,
            skipped = tally.Skipped,
            failedMenus = tally.FailedMenus.Count,
        };
        if (tally.Skipped > 0)
            CreoLog.Warn(summary, @event: "menu.registration.summary", module: "CreoAppHost", layer: CreoLogLayer.App, props: props);
        else
            CreoLog.Info(summary, @event: "menu.registration.summary", module: "CreoAppHost", layer: CreoLogLayer.App, props: props);
    }

    private static bool IsParentFailed(MenuTally tally, string parentMenu, string kind, string itemName)
    {
        if (!tally.FailedMenus.Contains(parentMenu))
            return false;

        tally.Skipped++;
        CreoLog.Trace($"{kind} 跳过 item={itemName} parent={parentMenu}(父菜单已失败)",
            @event: "menu.child.skip", module: "CreoAppHost", layer: CreoLogLayer.App,
            props: new { parent = parentMenu, item = itemName, kind, reason = "parent-failed" });
        return true;
    }

    private static void Check(int rc, string operation)
    {
        if (rc != 0)
            throw new CreoBridgeException($"{operation} failed rc={rc}");
    }

    private sealed class MenuTally
    {
        internal HashSet<string> FailedMenus { get; } = new(StringComparer.Ordinal);
        internal int Registered { get; set; }
        internal int Skipped { get; set; }
    }
}
