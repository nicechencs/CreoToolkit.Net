using CreoToolkit.Interop;
using CreoToolkit.Sdk;

namespace CreoToolkit.App;

/// <summary>
/// option command actFn 执行上下文。提供 <see cref="MenuBridge"/> 直读直写 uiCmdValue 的便利，
/// 也保留 <see cref="Session"/> + <see cref="Messages"/> 与普通命令对齐。
/// </summary>
public sealed class CreoOptionActionContext
{
    public CreoSession? Session { get; }
    public CreoMessages Messages { get; }
    public IMenuBridge MenuBridge { get; }
    public nint CmdId { get; }
    public nint PValue { get; }

    internal CreoOptionActionContext(CreoSession? session, CreoMessages messages,
        IMenuBridge menuBridge, nint cmdId, nint pValue)
    {
        Session = session;
        Messages = messages ?? throw new ArgumentNullException(nameof(messages));
        MenuBridge = menuBridge ?? throw new ArgumentNullException(nameof(menuBridge));
        CmdId = cmdId;
        PValue = pValue;
    }

    /// <summary>读 check 当前值（actFn 内可用；valFn 内不可用 — 加强项 1）。</summary>
    public bool GetCheckValue()
    {
        MenuBridge.ChkbuttonValueGet(PValue, out var v);
        return v;
    }

    /// <summary>写 check 值。</summary>
    public void SetCheckValue(bool value) => MenuBridge.ChkbuttonValueSet(PValue, value);

    /// <summary>读 radio 当前选中项 name。</summary>
    public string GetRadioValue()
    {
        MenuBridge.RadiogrpValueGet(PValue, out var n);
        return n;
    }

    /// <summary>写 radio 选中项 name。</summary>
    public void SetRadioValue(string itemName) => MenuBridge.RadiogrpValueSet(PValue, itemName);
}

/// <summary>
/// option command valFn 执行上下文（加强项 1：只能写 uiCmdValue，不能查 Creo / 不能分配）。
/// 没有 <see cref="CreoSession"/> 暴露面，强制 valFn 自治：从 .NET 端 cached state 写出去。
/// </summary>
public sealed class CreoOptionValueContext
{
    public IMenuBridge MenuBridge { get; }
    public nint CmdId { get; }
    public nint PValue { get; }

    internal CreoOptionValueContext(IMenuBridge menuBridge, nint cmdId, nint pValue)
    {
        MenuBridge = menuBridge ?? throw new ArgumentNullException(nameof(menuBridge));
        CmdId = cmdId;
        PValue = pValue;
    }

    public void SetCheckValue(bool value) => MenuBridge.ChkbuttonValueSet(PValue, value);
    public void SetRadioValue(string itemName) => MenuBridge.RadiogrpValueSet(PValue, itemName);
}

/// <summary>option command actFn handler（用户点击触发）。</summary>
public delegate void CreoOptionActionHandler(CreoOptionActionContext ctx);

/// <summary>option command valFn handler（Creo 询问当前值时触发；加强项 1：只写不查）。</summary>
public delegate void CreoOptionValueHandler(CreoOptionValueContext ctx);

/// <summary>
/// option command 声明（radio/check 用 ProCmdOptionAdd 注册的命令）。
/// 与普通 <see cref="CreoCommand"/> 区分：option command 不进 token dispatch 表，
/// 走纯 .NET callback hub（GCHandle 钉持），无路由开销。
/// </summary>
public sealed class CreoOptionCommand
{
    public CreoOptionCommand(string name, string labelKey, string helpKey,
        bool defaultBoolean,
        CreoOptionActionHandler actionHandler,
        CreoOptionValueHandler valueHandler)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        LabelKey = labelKey ?? "";
        HelpKey = helpKey ?? "";
        DefaultBoolean = defaultBoolean;
        ActionHandler = actionHandler ?? throw new ArgumentNullException(nameof(actionHandler));
        ValueHandler = valueHandler ?? throw new ArgumentNullException(nameof(valueHandler));
    }

    public string Name { get; }
    public string LabelKey { get; }
    public string HelpKey { get; }
    public bool DefaultBoolean { get; }
    public CreoOptionActionHandler ActionHandler { get; }
    public CreoOptionValueHandler ValueHandler { get; }
}
