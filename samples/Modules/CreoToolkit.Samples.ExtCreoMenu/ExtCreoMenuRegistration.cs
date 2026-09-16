// 移植自 https://github.com/slacker-HD/creo_toolkit/tree/master/CreoMenuExample
// 源仓库 commit (master HEAD clone)
// License: 未声明 / unknown — Registration.cs / csproj / 资源文件均未复用源仓代码，
// 仅参照 user_initialize / user_terminate 行为 + 菜单层级结构作 .NET 端独立实现。
//
// 原 demo 包含 5 个功能项：
//   1. 顶级菜单 CreoMenuExample（neighbor=Help）
//   2. 子菜单 MainMenu / MainMenuItem（普通 push button + 弹消息）
//   3. 子菜单 RadioButtonMenu / 4 个 radio 项
//   4. 子菜单 CheckButtonMenu / 1 个 check 项
//   5. popup notification (PRO_POPUPMENU_CREATE_POST=72) + 自定义 ribbon
//
// 复刻范围：项 1-5 中前 5 项全部完整实现；ribbon (.rbn 二进制文件) 跳过。
using CreoToolkit.App;

namespace CreoToolkit.Samples.ExtCreoMenu;

public static class ExtCreoMenuRegistration
{
    private const string LogModule = "ExtCreoMenu";


    // radio/check 的 cached state（加强项 1：valFn 只读 .NET 缓存写 uiCmdValue，不调 Creo 查询）。
    // sample 自管：actFn 切换 state 时同时调 ctx.SetCheckValue/SetRadioValue。
    private static string _currentRadio = "RadioButtonMenu1";
    private static bool _currentCheck = false;

    public static void Register(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);

        // ===== 1) 顶级菜单 =====
        app.MenuAdd(
            menuName: "ExtCreoMenu",
            menuLabel: "EXT_CREO_MENU_LABEL",
            neighbor: "Help",
            addAfter: false);

        // ===== 2) MainMenu 子菜单 + MainMenuItem 普通按钮 =====
        app.SubMenu(
            parentMenu: "ExtCreoMenu",
            menuName: "ExtMainMenu",
            menuLabel: "EXT_CREO_MENU_MAIN_LABEL");

        app.Command("ext.creo-menu.main", "EXT_CREO_MENU_MAIN_ITEM_LABEL", "EXT_CREO_MENU_MAIN_ITEM_HELP", ctx =>
        {
            CreoAppLog.Info("ext.creo-menu.main invoked",
                @event: "ext.creo-menu.main.invoked",
                module: LogModule);
            ctx.Messages.Info("EXT_CREO_MENU_MAIN_DIALOG");
        });
        app.MenuButton(
            parentMenu: "ExtMainMenu",
            buttonName: "ExtMainItem",
            commandName: "ext.creo-menu.main");

        // ===== 3) RadioButtonMenu 子菜单 + 4 项 radio 组 =====
        app.SubMenu(
            parentMenu: "ExtCreoMenu",
            menuName: "ExtRadioMenu",
            menuLabel: "EXT_CREO_MENU_RADIO_LABEL");

        app.OptionCommand("ext.creo-menu.radio",
            labelKey: "EXT_CREO_MENU_RADIO_GROUP",
            helpKey: "EXT_CREO_MENU_RADIO_GROUP",
            defaultBoolean: false,
            actHandler: ctx =>
            {
                var picked = ctx.GetRadioValue();
                _currentRadio = picked;
                CreoAppLog.Info($"ext.creo-menu.radio picked={picked}",
                    @event: "ext.creo-menu.radio.invoked",
                    module: LogModule,
                    props: new { picked });
                ctx.Messages.Info("EXT_CREO_MENU_RADIO_PICKED");
            },
            valHandler: ctx =>
            {
                // 加强项 1：只读 managed 缓存，写到 uiCmdValue（不调 Creo）
                ctx.SetRadioValue(_currentRadio);
            });

        var radioItems = new[]
        {
            new CreoRadioItem("RadioButtonMenu1", "EXT_CREO_MENU_RADIO_ITEM1_LABEL", "EXT_CREO_MENU_RADIO_ITEM1_HELP"),
            new CreoRadioItem("RadioButtonMenu2", "EXT_CREO_MENU_RADIO_ITEM2_LABEL", "EXT_CREO_MENU_RADIO_ITEM2_HELP"),
            new CreoRadioItem("RadioButtonMenu3", "EXT_CREO_MENU_RADIO_ITEM3_LABEL", "EXT_CREO_MENU_RADIO_ITEM3_HELP"),
            new CreoRadioItem("RadioButtonMenu4", "EXT_CREO_MENU_RADIO_ITEM4_LABEL", "EXT_CREO_MENU_RADIO_ITEM4_HELP"),
        };
        app.MenuRadioGroup(
            parentMenu: "ExtRadioMenu",
            groupName: "ExtRadioGroup",
            items: radioItems,
            optionCommandName: "ext.creo-menu.radio",
            description: "EXT_CREO_MENU_RADIO_DESC");

        // ===== 4) CheckButtonMenu 子菜单 + 1 项 check =====
        app.SubMenu(
            parentMenu: "ExtCreoMenu",
            menuName: "ExtCheckMenu",
            menuLabel: "EXT_CREO_MENU_CHECK_LABEL");

        app.OptionCommand("ext.creo-menu.check",
            labelKey: "EXT_CREO_MENU_CHECK_ITEM_LABEL",
            helpKey: "EXT_CREO_MENU_CHECK_ITEM_HELP",
            defaultBoolean: false,
            actHandler: ctx =>
            {
                _currentCheck = !_currentCheck;
                ctx.SetCheckValue(_currentCheck);
                CreoAppLog.Info($"ext.creo-menu.check toggled={_currentCheck}",
                    @event: "ext.creo-menu.check.invoked",
                    module: LogModule,
                    props: new { state = _currentCheck });
                ctx.Messages.Info("EXT_CREO_MENU_CHECK_TOGGLED");
            },
            valHandler: ctx =>
            {
                ctx.SetCheckValue(_currentCheck);
            });
        app.MenuCheckButton(
            parentMenu: "ExtCheckMenu",
            buttonName: "ExtCheckItem",
            optionCommandName: "ext.creo-menu.check",
            labelKey: "EXT_CREO_MENU_CHECK_ITEM_LABEL",
            helpKey: "EXT_CREO_MENU_CHECK_ITEM_HELP");

        // ===== 5) Popup notification（PRO_POPUPMENU_CREATE_POST） =====
        app.PopupNotification(CreoPopupNotificationType.PopupMenuCreatePost, menuName =>
        {
            CreoAppLog.Info($"popup notification invoked menu={menuName}",
                @event: "ext.creo-menu.popup.invoked",
                module: LogModule,
                props: new { menuName });
        });

        // ===== 6) Ribbon =====
        // 自定义 ribbon (.rbn 二进制) 未实现。
        // 当前 sample 不调 app.LoadCustomRibbon(...)。
    }
}
