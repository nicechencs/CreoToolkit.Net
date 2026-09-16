namespace CreoToolkit.Interop;

/// <summary>
/// ExtCreoMenu 扩展面：菜单层级 / radio / check / option command / popup / notification / message。
/// 与 <see cref="ICommandBridge"/> 分接口，因为 net472 不支持 default interface methods（CS8701）——
/// 二级接口避开该限制。
/// <para>
/// <see cref="NativeCommandBridge"/> 同时实现 <see cref="ICommandBridge"/> 与本接口；测试 fake bridge 一般
/// 不实现本接口（菜单扩展功能在 fake 模式自动跳过：<see cref="CreoToolkit.App"/> 层 <c>as IMenuBridge</c>
/// cast 后 null check）。
/// </para>
/// <para>
/// **Callback 生命周期铁律**：bridge.Dispose 必须先 <see cref="NotificationUnset"/>
/// 再释放 GCHandle，否则 native 持悬空指针 → 段错误。<see cref="NativeCommandBridge"/> 的 Dispose 已落实该顺序。
/// </para>
/// </summary>
public interface IMenuBridge
{
    /// <summary>添加子菜单（嵌套层级）；parentMenu 必须是已存在的菜单名。</summary>
    int MenubarmenuMenuAdd(string parentMenu, string menuName, string menuLabel,
        string? neighbor, bool addAfter, string? messageFile);

    /// <summary>添加 check 菜单项；optionId 来自 <see cref="CommandOptionAdd"/>。</summary>
    int MenubarmenuChkbuttonAdd(string parentMenu, string checkButtonName,
        string labelKey, string? helpKey, string? neighbor, bool addAfter,
        nint optionId, string? messageFile);

    /// <summary>添加 radio 组菜单项；items 内含每项的 name/label/help/icon。</summary>
    int MenubarmenuRadiogrpAdd(string parentMenu, string groupName,
        IReadOnlyList<RadioGroupItem> items,
        string? neighbor, bool addAfter, nint optionId, string? messageFile);

    /// <summary>
    /// 注册带选项命令（radio/check 用 ProCmdOptionAdd）。actionHandler/valueHandler 经 callback hub
    /// GCHandle 钉持，函数指针在 bridge 存活期间稳定。defaultBoolean 对应 PRO_B_TRUE/FALSE（check 初值）。
    /// </summary>
    int CommandOptionAdd(string actionName, bool defaultBoolean,
        OptionCmdActHandler actionHandler, OptionCmdValHandler valueHandler,
        out nint commandId);

    /// <summary>设置命令图标（ASCII char[81]）。</summary>
    int CommandIconSet(nint commandId, string iconName);

    /// <summary>暴露 radio 组到 Ribbon / Customize UI。</summary>
    int CommandRadiogrpDesignate(nint commandId, IReadOnlyList<RadioGroupItem> items,
        string? description, string? messageFile);

    /// <summary>按命令名查找 cmd id（popup notification 内部 dispatch 用）。</summary>
    int CommandCmdIdFind(string commandName, out nint commandId);

    /// <summary>从 uiCmdValue* 读 check 当前值（valFn 内只查 managed 缓存,不回调 native）。</summary>
    int ChkbuttonValueGet(nint pValue, out bool value);

    /// <summary>向 uiCmdValue* 写 check 值。</summary>
    int ChkbuttonValueSet(nint pValue, bool value);

    /// <summary>从 uiCmdValue* 读 radio 当前选中项 name。</summary>
    int RadiogrpValueGet(nint pValue, out string itemName);

    /// <summary>向 uiCmdValue* 写 radio 选中项 name。</summary>
    int RadiogrpValueSet(nint pValue, string itemName);

    /// <summary>
    /// 注册通知 handler（popup notification 等）。handler 经 hub 钉持。
    /// Dispose 顺序铁律见接口注释。
    /// </summary>
    int NotificationSet(int notifyType, PopupNotifyHandler handler);

    /// <summary>反注册通知 handler；释放 GCHandle。</summary>
    int NotificationUnset(int notifyType);

    /// <summary>查 popup 菜单 id。</summary>
    int PopupmenuIdGet(string name, out int menuId);

    /// <summary>在 popup 菜单中添加按钮，绑定到已注册命令；position=-1 等价 PRO_VALUE_UNUSED。</summary>
    int PopupmenuButtonAdd(int menuId, int position, string buttonName,
        string buttonLabel, string buttonHelp, nint commandId);

    /// <summary>从消息文件读 key 到 string（ProLine=wchar_t[81]，PRO_LINE_SIZE 含 NUL，已 trim '\0'）。</summary>
    int MessageToBuffer(string messageFile, string messageKey, out string text);
}
