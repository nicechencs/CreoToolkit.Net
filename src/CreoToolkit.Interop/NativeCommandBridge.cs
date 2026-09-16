using System.Runtime.InteropServices;
using System.Text;
using CreoToolkit.Interop.Generated;   // ProBooleans / pro_notify_type 直绑

namespace CreoToolkit.Interop;

/// <summary>
/// 真实命令桥：转调 <see cref="NativeMethods"/> 的命令桥导出。
/// <para>
/// 退役后分两段：BridgeInitialize/Terminate/CommandActionAdd/LastCommandStatusGet 仍走 <c>Ctk_*</c>
/// (维护 g_dispatch/g_routes 路由 + SEH trampoline)；MessageDisplay/CommandDesignate/RibbonDefinitionfileLoad
/// /MenubarPushbuttonAdd 直绑 <c>Pro*</c>，原 native 端的 null 兜底语义平移到本类。
/// </para>
/// <para>
/// **ExtCreoMenu 扩展**：同时实现 <see cref="IMenuBridge"/>，新增 13 个菜单层级 / radio / check /
/// option command / popup / notification / message helper 方法。option/notification callback 经 <see cref="OptionCallbackHub"/>
/// + <see cref="NotificationCallbackHub"/> 钉持（纯 .NET，不踩 G2 准入门）。
/// </para>
/// <see cref="Dispose"/> 先使 command dispatch inert，再卸载其余 callback hub；若 native
/// callback 仍在执行，终止会失败而不会释放任何仍可能被调用的 managed root。
/// 脱 Creo 单测不实例化本类（会触发真实 P/Invoke），改用测试 fake。
/// </summary>
public sealed class NativeCommandBridge : ICommandBridge, ICommandBridgeCapacity, IMenuBridge
{
    /// <summary>
    /// <see cref="MessageDisplay"/> 的固定 message key——保持原 native <c>Ctk_MessageDisplay</c>
    /// 行为(硬编码 <c>"CTK_USER_MSG"</c>)，避免外部 API 多一个参数。设为 <c>internal</c> 让测试可校验。
    /// </summary>
    internal const string DefaultUserMessageKey = "CTK_USER_MSG";

    private readonly CallbackRegistry _callbackRegistry = new();
    private readonly OptionCallbackHub _optionHub;
    private readonly NotificationCallbackHub _notifyHub;
    private int _disposed;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _ownsCommandBridge;

    public NativeCommandBridge()
    {
        _optionHub = new OptionCallbackHub(_callbackRegistry);
        _notifyHub = new NotificationCallbackHub(_callbackRegistry);
    }

    // ========== ICommandBridge（原有 10 方法）==========

    public int BridgeInitialize(nint dispatch)
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("NativeCommandBridge.Initialize must run on its creating thread.");
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(NativeCommandBridge));
        var rc = NativeMethods.Ctk_CommandBridgeInitialize(dispatch);
        if (rc == 0)
            _ownsCommandBridge = true;
        return rc;
    }

    public int CommandCapacityReserve(int commandCount)
        => NativeMethods.Ctk_CommandBridgeReserve(commandCount);

    public int BridgeTerminate() => NativeMethods.Ctk_CommandBridgeTerminate();

    public int CommandActionAdd(string actionName, int token, int priority,
        bool allowInNonActiveWindow, bool allowInAccessoryWindow, out nint commandId)
        => NativeMethods.Ctk_CommandActionAdd(actionName, token, priority,
            allowInNonActiveWindow ? 1 : 0, allowInAccessoryWindow ? 1 : 0, out commandId);

    /// <summary>null helpKey/descriptionKey 兜底 labelKey；null messageFile 兜底空串(平移原 native 行为)。</summary>
    public int CommandDesignate(nint commandId, string labelKey, string? helpKey, string? descriptionKey, string? messageFile)
        => (int)Generated.NativeMethods.ProCmdDesignate(commandId, labelKey,
            helpKey ?? labelKey,
            descriptionKey ?? labelKey,
            messageFile ?? string.Empty);

    /// <summary>固定调 <c>ProMessageDisplay(messageFile, "CTK_USER_MSG", text)</c>(平移原 native 硬编码)。</summary>
    public int MessageDisplay(string messageFile, string text)
        => NativeMethods.ProMessageDisplay(messageFile, DefaultUserMessageKey, text);

    public int RibbonDefinitionfileLoad(string ribbonFile)
        => (int)Generated.NativeMethods.ProRibbonDefinitionfileLoad(ribbonFile);

    public int LastCommandStatusGet(out int token, out int status)
        => NativeMethods.Ctk_LastCommandStatusGet(out token, out status);

    public int MenubarMenuAdd(string menuName, string menuLabel,
        string? neighbor, bool addAfter, string? messageFile)
        => (int)Generated.NativeMethods.ProMenubarMenuAdd(
            menuName,
            menuLabel,
            neighbor ?? string.Empty,
            (ProBooleans)(addAfter ? 1 : 0),
            messageFile ?? string.Empty);

    /// <summary>
    /// null helpKey 兜底 labelKey；null neighbor/messageFile 兜底空串(平移原 native ctk_app.cpp:265-271 行为；
    /// 原 native 还含 "labelKey 为 null 兜底 buttonName"，但接口签名 labelKey 是非 null <c>string</c>，
    /// 该分支不可达——保留为单层兜底, 与接口语义对齐)。
    /// </summary>
    public int MenubarPushbuttonAdd(string parentMenu, string buttonName,
        string labelKey, string? helpKey,
        string? neighbor, bool addAfter,
        nint commandId, string? messageFile)
        => (int)Generated.NativeMethods.ProMenubarmenuPushbuttonAdd(
            parentMenu, buttonName, labelKey,
            helpKey ?? labelKey,
            neighbor ?? string.Empty,
            (ProBooleans)(addAfter ? 1 : 0),
            commandId,
            messageFile ?? string.Empty);

    // ========== IMenuBridge（新增 13 方法）==========

    public int MenubarmenuMenuAdd(string parentMenu, string menuName, string menuLabel,
        string? neighbor, bool addAfter, string? messageFile)
        => (int)Generated.NativeMethods.ProMenubarmenuMenuAdd(parentMenu, menuName, menuLabel,
            neighbor ?? string.Empty, (ProBooleans)(addAfter ? 1 : 0), messageFile ?? string.Empty);

    public int MenubarmenuChkbuttonAdd(string parentMenu, string checkButtonName,
        string labelKey, string? helpKey, string? neighbor, bool addAfter,
        nint optionId, string? messageFile)
        => (int)Generated.NativeMethods.ProMenubarmenuChkbuttonAdd(parentMenu, checkButtonName, labelKey,
            helpKey ?? labelKey, neighbor ?? string.Empty, (ProBooleans)(addAfter ? 1 : 0),
            optionId, messageFile ?? string.Empty);

    public int MenubarmenuRadiogrpAdd(string parentMenu, string groupName,
        IReadOnlyList<RadioGroupItem> items,
        string? neighbor, bool addAfter, nint optionId, string? messageFile)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (items.Count == 0) throw new ArgumentException("radio group items cannot be empty", nameof(items));

        // 拼三段连续 ascii buffer：names[N][32] / labels[N][81] / helps[N][81]
        nint namesPtr = AllocAsciiBlock(items, 32, i => i.Name);
        nint labelsPtr = AllocAsciiBlock(items, 81, i => i.LabelKey);
        nint helpsPtr = AllocAsciiBlock(items, 81, i => i.HelpKey);
        try
        {
            return NativeMethods.ProMenubarmenuRadiogrpAdd(parentMenu, groupName, items.Count,
                namesPtr, labelsPtr, helpsPtr,
                neighbor ?? string.Empty, addAfter ? 1 : 0,
                optionId, messageFile ?? string.Empty);
        }
        finally
        {
            Marshal.FreeHGlobal(namesPtr);
            Marshal.FreeHGlobal(labelsPtr);
            Marshal.FreeHGlobal(helpsPtr);
        }
    }

    public int CommandOptionAdd(string actionName, bool defaultBoolean,
        OptionCmdActHandler actionHandler, OptionCmdValHandler valueHandler,
        out nint commandId)
    {
        commandId = IntPtr.Zero;
        var (actPtr, valPtr, ticket) = _optionHub.Wrap(actionHandler, valueHandler);
        int rc;
        try
        {
            rc = (int)Generated.NativeMethods.ProCmdOptionAdd(actionName,
                actPtr, (ProBooleans)(defaultBoolean ? 1 : 0), valPtr, IntPtr.Zero,
                (ProBooleans)1, (ProBooleans)0, out commandId);
        }
        catch
        {
            // native 未接管函数指针即抛出；把本次 wrap 精确摘除，避免失败项滞留 hub
            // 造成 Dispose 时误判为需隔离。
            _optionHub.Rollback(ticket);
            throw;
        }
        // ProCmdOptionAdd 无反注册 API：只要成功一次即终身持函数指针，不许 Rollback。
        // 失败时（rc != PRO_TK_NO_ERROR）native 未接管，安全撤销本次 wrap。
        if (rc != (int)Generated.ProErrors.PRO_TK_NO_ERROR)
            _optionHub.Rollback(ticket);
        return rc;
    }

    public int CommandIconSet(nint commandId, string iconName)
        => (int)Generated.NativeMethods.ProCmdIconSet(commandId, iconName);

    public int CommandRadiogrpDesignate(nint commandId, IReadOnlyList<RadioGroupItem> items,
        string? description, string? messageFile)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));
        if (items.Count == 0) throw new ArgumentException("radio group items cannot be empty", nameof(items));

        // names[N][32] / labels[N][81] / helps[N][81] / icons[N][81]（icon 为 null 时用空串）
        nint namesPtr = AllocAsciiBlock(items, 32, i => i.Name);
        nint labelsPtr = AllocAsciiBlock(items, 81, i => i.LabelKey);
        nint helpsPtr = AllocAsciiBlock(items, 81, i => i.HelpKey);
        nint iconsPtr = AllocAsciiBlock(items, 81, i => i.IconName ?? string.Empty);
        try
        {
            return NativeMethods.ProCmdRadiogrpDesignate(commandId, items.Count,
                namesPtr, labelsPtr, helpsPtr, iconsPtr,
                description ?? string.Empty, messageFile ?? string.Empty);
        }
        finally
        {
            Marshal.FreeHGlobal(namesPtr);
            Marshal.FreeHGlobal(labelsPtr);
            Marshal.FreeHGlobal(helpsPtr);
            Marshal.FreeHGlobal(iconsPtr);
        }
    }

    public int CommandCmdIdFind(string commandName, out nint commandId)
        => (int)Generated.NativeMethods.ProCmdCmdIdFind(commandName, out commandId);

    public int ChkbuttonValueGet(nint pValue, out bool value)
    {
        int rc = NativeMethods.ProMenubarmenuChkbuttonValueGet(pValue, out int raw);
        value = raw != 0;
        return rc;
    }

    public int ChkbuttonValueSet(nint pValue, bool value)
        => NativeMethods.ProMenubarmenuChkbuttonValueSet(pValue, value ? 1 : 0);

    public int RadiogrpValueGet(nint pValue, out string itemName)
    {
        var buffer = new byte[32];
        int rc = NativeMethods.ProMenubarMenuRadiogrpValueGet(pValue, buffer);
        itemName = DecodeAsciiZeroTerminated(buffer);
        return rc;
    }

    public int RadiogrpValueSet(nint pValue, string itemName)
    {
        var buffer = EncodeAsciiToFixed(itemName, 32);
        return NativeMethods.ProMenubarMenuRadiogrpValueSet(pValue, buffer);
    }

    public int NotificationSet(int notifyType, PopupNotifyHandler handler)
    {
        // 事务式覆盖：新 callback 先建立 GC root；native 接受后才释放旧 root。
        // Set 失败则只撤销候选 root，旧 callback 仍可被 native 安全调用。
        var candidate = _notifyHub.Prepare(handler);
        nint fnPtr = Marshal.GetFunctionPointerForDelegate(candidate);
        int rc;
        try
        {
            rc = (int)Generated.NativeMethods.ProNotificationSet((pro_notify_type)notifyType, fnPtr);
        }
        catch
        {
            // native 未接管候选函数指针即抛出：结构对齐 CommandOptionAdd 的失败回滚,
            // 撤销尚未 Commit 的候选 root 避免滞留 hub 造成 Dispose 时误判为需隔离。
            _notifyHub.Rollback(candidate);
            throw;
        }
        if (rc == (int)Generated.ProErrors.PRO_TK_NO_ERROR)
            _notifyHub.Commit(notifyType, candidate);
        else
            _notifyHub.Rollback(candidate);
        return rc;
    }

    public int NotificationUnset(int notifyType)
    {
        int rc = (int)Generated.NativeMethods.ProNotificationUnset((pro_notify_type)notifyType);
        if (NotificationUnsetReleasedCallback(rc))
            _notifyHub.Unregister(notifyType);
        return rc;
    }

    /// <summary>
    /// 只有成功或 native 明确表示注册已不存在时，才可释放 callback GC root。
    /// 其他失败都必须保留 root，避免 Creo 持有悬空函数指针。
    /// </summary>
    internal static bool NotificationUnsetReleasedCallback(int rc)
        => rc is (int)Generated.ProErrors.PRO_TK_NO_ERROR
            or (int)Generated.ProErrors.PRO_TK_E_NOT_FOUND
            or (int)Generated.ProErrors.PRO_TK_NOT_EXIST;

    public int PopupmenuIdGet(string name, out int menuId)
        => (int)Generated.NativeMethods.ProPopupmenuIdGet(name, out menuId);

    public int PopupmenuButtonAdd(int menuId, int position, string buttonName,
        string buttonLabel, string buttonHelp, nint commandId)
        => (int)Generated.NativeMethods.ProPopupmenuButtonAdd(menuId, position, buttonName,
            buttonLabel, buttonHelp, commandId, IntPtr.Zero, IntPtr.Zero);

    public int MessageToBuffer(string messageFile, string messageKey, out string text)
    {
        // PRO_LINE_SIZE=81 含 NUL：最多写 80 字符 + '\0'；DecodeWideZeroTerminated 按 NUL 截断。
        var buffer = new char[81];
        int rc = NativeMethods.ProMessageToBuffer(buffer, messageFile, messageKey);
        text = DecodeWideZeroTerminated(buffer);
        return rc;
    }

    // ========== Dispose（callback 保活顺序铁律）==========

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("NativeCommandBridge.Dispose must run on its creating thread.");
        if (Volatile.Read(ref _disposed) != 0) return;

        // Ctk_CommandBridgeTerminate is the ownership hand-off for the command
        // dispatch delegate. It fails fast when called reentrantly from that
        // callback; in that case do not release any managed callback roots.
        // CreoAppHost leaves its CommandDispatchPin alive and a later, non-
        // reentrant Dispose can retry.
        // If app registration failed before initialize (or initialize refused a
        // duplicate owner), this instance has no native command pointer to clear.
        // In particular, it must not terminate some other instance's bridge.
        int terminateRc = _ownsCommandBridge ? BridgeTerminate() : 0;
        if (terminateRc != 0)
            throw new InvalidOperationException(
                $"Ctk_CommandBridgeTerminate failed rc={terminateRc}; command callback is still active or the caller is not the bridge thread.");

        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // 1) The command dispatch is now inert. Unset other native callbacks in
        // reverse registration order; their roots retain the existing quarantine
        // policy below.
        //    成功 / 明确 not-found 视为 native 已释放函数指针，允许摘除对应 hub 项；
        //    其他失败或抛出保留 hub 中该 type 项，等待步骤 2 的隔离决策。
        var registeredTypes = _notifyHub.RegisteredTypes;
        for (int i = registeredTypes.Length - 1; i >= 0; i--)
        {
            var type = registeredTypes[i];
            try
            {
                int rc = (int)Generated.NativeMethods.ProNotificationUnset((pro_notify_type)type);
                if (NotificationUnsetReleasedCallback(rc))
                    _notifyHub.Unregister(type);
            }
            catch
            {
                // native 仍可能持函数指针；hub 内该 type 项保留供步骤 2 判定。
            }
        }
        // 2) notification hub：若仍有 unset 失败/未确认释放的 type,把整个 hub 加入进程级隔离集
        //    (静态可达 → _byType 字典强引用 → native 委托实例 → thunk 稳定),不 Dispose hub。
        //    全部 unset 成功才 Dispose,常态零泄漏。
        if (_notifyHub.RegisteredTypes.Length > 0)
            CallbackQuarantine.Add(_notifyHub);
        else
            _notifyHub.Dispose();
        // 3) option hub：ProCmdOptionAdd 无反注册 API,只要有过成功注册就必须隔离到进程终止。
        //    hub._actDelegates/_valDelegates 列表强引用 native 委托实例,静态可达即保活。
        //    未注册过（HasRegistrations=false）则 Dispose,常态零泄漏。
        if (_optionHub.HasRegistrations)
            CallbackQuarantine.Add(_optionHub);
        else
            _optionHub.Dispose();
        // 4) 释放 registry 里所有 GCHandle：对已隔离对象无害（thunk 靠委托对象可达性,不靠
        //    GCHandle）；对未隔离的 hub 则彻底清理。
        _callbackRegistry.Dispose();
        // Command bridge termination happened before callback roots could be
        // released. Its route identities intentionally remain native-lifetime.
    }

    // ========== 内部 helpers ==========

    /// <summary>把 N 个 string 编码为连续 ASCII 定长 buffer（每项 perItemSize 字节，'\0' 填充）。</summary>
    private static nint AllocAsciiBlock(IReadOnlyList<RadioGroupItem> items, int perItemSize,
        Func<RadioGroupItem, string> getString)
    {
        int total = items.Count * perItemSize;
        nint ptr = Marshal.AllocHGlobal(total);
        try
        {
            // 全部清零 '\0'
            for (int i = 0; i < total; i++) Marshal.WriteByte(ptr, i, 0);
            for (int i = 0; i < items.Count; i++)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(getString(items[i]) ?? string.Empty);
                int len = Math.Min(bytes.Length, perItemSize - 1); // 留 1 字节给 '\0'
                Marshal.Copy(bytes, 0, ptr + i * perItemSize, len);
            }
            return ptr;
        }
        catch
        {
            Marshal.FreeHGlobal(ptr);
            throw;
        }
    }

    /// <summary>从 ASCII 定长 byte buffer 读 string（找首个 '\0' 截断）。</summary>
    private static string DecodeAsciiZeroTerminated(byte[] buffer)
    {
        int len = 0;
        while (len < buffer.Length && buffer[len] != 0) len++;
        return Encoding.ASCII.GetString(buffer, 0, len);
    }

    /// <summary>编码 string 到 ASCII 定长 byte buffer（超长截断，末尾 '\0'）。</summary>
    private static byte[] EncodeAsciiToFixed(string value, int size)
    {
        var buffer = new byte[size];
        byte[] bytes = Encoding.ASCII.GetBytes(value ?? string.Empty);
        int len = Math.Min(bytes.Length, size - 1);
        Buffer.BlockCopy(bytes, 0, buffer, 0, len);
        return buffer;
    }

    /// <summary>从 wchar 定长 char buffer 读 string（找首个 '\0' 截断）。</summary>
    private static string DecodeWideZeroTerminated(char[] buffer)
    {
        int len = 0;
        while (len < buffer.Length && buffer[len] != '\0') len++;
        return new string(buffer, 0, len);
    }
}
