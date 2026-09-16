namespace CreoToolkit.Interop;

/// <summary>uiCmdCmdActFn 头文件 typedef 在 .NET 端的"友好"映射（无 appData 参数，因头文件文档 "Not used"）。</summary>
public delegate void OptionCmdActHandler(nint cmdId, nint pValue);

/// <summary>uiCmdCmdValFn 头文件 typedef 在 .NET 端映射；加强项 1：实现内只读 managed 缓存写 uiCmdValue,
/// 不在内做 Creo 查询/分配/锁竞争。</summary>
public delegate void OptionCmdValHandler(nint cmdId, nint pValue);

/// <summary>PRO_POPUPMENU_CREATE_POST 通知 handler；menuName 是 popup 菜单名（已从 char[32] 解码为 string）。</summary>
public delegate void PopupNotifyHandler(string menuName);

/// <summary>radio 组单个项的元数据。net472 缺 <c>IsExternalInit</c> polyfill,故不用 record。</summary>
public sealed class RadioGroupItem
{
    public RadioGroupItem(string name, string labelKey, string helpKey, string? iconName = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        LabelKey = labelKey ?? throw new ArgumentNullException(nameof(labelKey));
        HelpKey = helpKey ?? throw new ArgumentNullException(nameof(helpKey));
        IconName = iconName;
    }

    public string Name { get; }
    public string LabelKey { get; }
    public string HelpKey { get; }
    public string? IconName { get; }
}

/// <summary>
/// 命令桥 public 接缝：App 层只依赖此接口（真实 <see cref="NativeCommandBridge"/> 与测试 fake 各自实现），
/// 从而避开 internal 的 <see cref="NativeMethods"/>。布尔参数在实现里折成 native 的 0/1。
/// <para>
/// **ExtCreoMenu 扩展面**：菜单层级 / radio / check / option command / popup / notification / message
/// 这些新增能力在二级接口 <see cref="IMenuBridge"/>（NativeCommandBridge 同时实现两者；fake 不实现扩展面，
/// CreoAppHost 用 <c>as</c> cast 取，扩展功能在 fake 模式自动跳过）。
/// 之所以分接口而不放本接口 default impl：net472 不支持 default interface methods（CS8701），
/// 二级接口避开该限制。
/// </para>
/// </summary>
public interface ICommandBridge : IDisposable
{
    /// <summary>安装钉住后的 dispatch 函数指针。</summary>
    int BridgeInitialize(nint dispatch);

    /// <summary>卸载 dispatch 并清空路由表。</summary>
    int BridgeTerminate();

    /// <summary>注册命令，输出 cmd_id；失败时 commandId=0。</summary>
    int CommandActionAdd(string actionName, int token, int priority,
        bool allowInNonActiveWindow, bool allowInAccessoryWindow, out nint commandId);

    /// <summary>暴露命令到 Customize UI / ribbon。helpKey/descriptionKey/messageFile 可空。</summary>
    int CommandDesignate(nint commandId, string labelKey, string? helpKey, string? descriptionKey, string? messageFile);

    /// <summary>在消息区弹出一条用户消息。</summary>
    int MessageDisplay(string messageFile, string text);

    /// <summary>加载 ribbon 定义文件。</summary>
    int RibbonDefinitionfileLoad(string ribbonFile);

    /// <summary>取最近一次回调的 token 与 dispatch 返回码。</summary>
    int LastCommandStatusGet(out int token, out int status);

    /// <summary>
    /// 创建传统菜单栏顶级菜单。必须先创建自定义父菜单,再挂按钮。
    /// menuName/menuLabel/neighbor 是 ASCII;messageFile 可空,实现层兜底为空串。
    /// </summary>
    int MenubarMenuAdd(string menuName, string menuLabel,
        string? neighbor, bool addAfter, string? messageFile);

    /// <summary>
    /// 在传统菜单栏添加按钮,绑定到已注册的命令。
    /// parentMenu/buttonName 是菜单 path 段(ASCII),label/help 是 msg key,neighbor 可空,addAfter 控制插入位置。
    /// </summary>
    int MenubarPushbuttonAdd(string parentMenu, string buttonName,
        string labelKey, string? helpKey,
        string? neighbor, bool addAfter,
        nint commandId, string? messageFile);
}

/// <summary>
/// 可选的命令 route 容量协定。旧的 <see cref="ICommandBridge"/> fake 和外部实现无需实现它；
/// 真实 native bridge 实现它，使 host 可在不可回滚的命令注册之前完成全部内存分配。
/// </summary>
public interface ICommandBridgeCapacity
{
    /// <summary>预留本次应用的完整命令数；必须在任何 CommandActionAdd 前调用。</summary>
    int CommandCapacityReserve(int commandCount);
}
