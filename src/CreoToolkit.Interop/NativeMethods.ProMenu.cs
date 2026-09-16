using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// Pro* 直绑手写 P/Invoke 保留区。
/// <para>
/// 历史:ExtCreoMenu 移植新增 16 个手写 wrapper(签名走 LPStr/LPWStr,绕开
/// Generated [Out, LPArray] marshalling bug)。
/// </para>
/// <para>
/// gen_bindings.py 加 manifest fixed_buffers schema 后,9 个手写已退役
/// (ProMenubarmenuMenuAdd / ProMenubarmenuChkbuttonAdd / ProCmdOptionAdd / ProCmdIconSet /
/// ProCmdCmdIdFind / ProPopupmenuIdGet / ProPopupmenuButtonAdd / ProNotificationSet /
/// ProNotificationUnset)。caller 改走 <see cref="Generated.NativeMethods"/> Generated LPStr/LPWStr 签名。
/// </para>
/// <para>
/// 本文件保留 **7 个** 手写,分两类:
/// </para>
/// <list type="bullet">
///   <item><b>Generated 缺产物 / 形态复杂 (2 个)</b>: ProMenubarmenuRadiogrpAdd / ProCmdRadiogrpDesignate
///   - 含 nint itemNames/itemLabels/itemHelps 三段 ASCII 拼装 buffer(C# 端 Marshal.AllocHGlobal),
///     generator 当前不识别"N×fixed-byte buffer 拼接"形态。</item>
///   <item><b>ref uiCmdValue 签名陷阱 (4 个)</b>: ProMenubarmenuChkbuttonValueGet/Set /
///   ProMenubarMenuRadiogrpValueGet/Set
///   - 手写用 <c>nint pValue</c>(避免 ref struct marshalling 风险),Generated 用
///     <c>ref uiCmdValue cmd_value</c>。caller 适配需 <c>Unsafe.AsRef&lt;uiCmdValue&gt;((void*)pValue)</c>
///     unsafe code,复杂度高。当前 caller 接受 <c>nint pValue</c> 是 NativeCommandBridge 公开 API,改之
///     向上传播到 IMenuBridge → App。后续真业务触发时再考虑迁移。</item>
///   <item><b>真 out wchar buffer (1 个)</b>: ProMessageToBuffer
///   - <c>buffer = wchar_t[81]</c> 真 out buffer(从 msg 文件读 ProLine),Generated 缺产物。</item>
/// </list>
/// <para>
/// **Callback typedef**:这里保留的 ProNotificationSet/Unset 已迁 Generated,但
/// callback delegate 定义仍需在 CreoToolkit.Interop 命名空间下(被 NotificationCallbackHub /
/// OptionCallbackHub 直接引用,见下方 typedef)。
/// </para>
/// <para>
/// 不踩 L1 准入门:所有 callback 走纯 .NET <see cref="Marshal.CallbackRegistry"/>
/// GCHandle 钉持,函数指针经 <see cref="Marshal.GetFunctionPointerForDelegate"/> 取,
/// 不需要 native trampoline。
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    // ====== Callback delegate typedef (Hub 直引,保留即可) ======

    /// <summary>uiCmdCmdActFn 头文件签名（3 参；p_push_command_data 永远 NULL）。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int uiCmdCmdActFn(nint cmdId, nint pValue, nint appData);

    /// <summary>uiCmdCmdValFn 头文件签名（2 参；valFn 只应读 managed 缓存写 uiCmdValue，不在内做 Creo 查询/分配）。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int uiCmdCmdValFn(nint cmdId, nint pValue);

    /// <summary>uiCmdAccessFn 签名；返回 uiCmdAccessState（int enum，ACCESS_AVAILABLE=8）。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int uiCmdAccessFn(int accessMode);

    /// <summary>PRO_POPUPMENU_CREATE_POST 通知签名 cast。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProPopupmenuNotifyFn(nint menuName);

    // ====== Radio 组 / Radiogrp Designate (含 nint 拼装 buffer,Generated 缺产物保留) ======

    /// <summary>
    /// 添加 radio 组菜单项。item_names/labels/helps 是 N 个定长 char 数组（[N][32]/[N][81]/[N][81]），
    /// C# 端经 <see cref="Marshal.AllocHGlobal(int)"/> 拼成连续 buffer 后传 IntPtr。
    /// **保留理由**: generator 不识别"N×fixed-byte buffer 拼接"形态,Generated 缺产物。
    /// </summary>
    [DllImport(Dll, EntryPoint = "ProMenubarmenuRadiogrpAdd", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMenubarmenuRadiogrpAdd(
        [MarshalAs(UnmanagedType.LPStr)] string parentMenu,
        [MarshalAs(UnmanagedType.LPStr)] string groupName,
        int nItems,
        nint itemNames,
        nint itemLabels,
        nint itemHelps,
        [MarshalAs(UnmanagedType.LPStr)] string neighbor,
        int addAfterNeighbor,
        nint optionId,
        [MarshalAs(UnmanagedType.LPWStr)] string msgFile);

    /// <summary>暴露 radio 组到 Customize UI / Ribbon。names/labels/helps/icons 同 ProMenubarmenuRadiogrpAdd 拼装方式。
    /// **保留理由**: 同上,"N×fixed-byte buffer 拼接"形态 generator 不识别。</summary>
    [DllImport(Dll, EntryPoint = "ProCmdRadiogrpDesignate", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProCmdRadiogrpDesignate(
        nint cmdId,
        int nItems,
        nint names,
        nint labels,
        nint helps,
        nint icons,
        [MarshalAs(UnmanagedType.LPStr)] string description,
        [MarshalAs(UnmanagedType.LPWStr)] string msgFile);

    // ====== check/radio 值读写 (ref uiCmdValue 签名陷阱保留,caller 用 nint pValue) ======

    /// <summary>**保留理由**: Generated 签名 `ref uiCmdValue cmd_value, out ProBooleans value`
    /// 与本手写 `nint pValue, out int value` 不兼容。caller(NativeCommandBridge.cs)接受 nint pValue 是
    /// IMenuBridge 公开 API,改之向上传播到 App 层。后续真业务触发时再考虑统一迁 ref struct。</summary>
    [DllImport(Dll, EntryPoint = "ProMenubarmenuChkbuttonValueGet", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMenubarmenuChkbuttonValueGet(nint pValue, out int value);

    /// <summary>**保留理由**: 同 <see cref="ProMenubarmenuChkbuttonValueGet"/>。</summary>
    [DllImport(Dll, EntryPoint = "ProMenubarmenuChkbuttonValueSet", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMenubarmenuChkbuttonValueSet(nint pValue, int value);

    /// <summary>**保留理由**: 同 <see cref="ProMenubarmenuChkbuttonValueGet"/>。
    /// Generated 用 `ref uiCmdValue, [Out, LPArray, U1] byte[]`,手写用 `nint pValue, [Out, LPArray] byte[]`。</summary>
    [DllImport(Dll, EntryPoint = "ProMenubarMenuRadiogrpValueGet", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMenubarMenuRadiogrpValueGet(
        nint pValue,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] byte[] name);

    /// <summary>**保留理由**: 同 <see cref="ProMenubarmenuChkbuttonValueGet"/>。
    /// Generated 用 `ref uiCmdValue, LPStr string`,手写用 `nint pValue, [In, LPArray] byte[]`(避免 marshal Encoding 路径)。</summary>
    [DllImport(Dll, EntryPoint = "ProMenubarMenuRadiogrpValueSet", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMenubarMenuRadiogrpValueSet(
        nint pValue,
        [In, MarshalAs(UnmanagedType.LPArray, SizeConst = 32, ArraySubType = UnmanagedType.U1)] byte[] name);

    // ====== Message helper (真 out wchar buffer,Generated 缺产物) ======

    /// <summary>从消息文件读 key 到 wchar 缓冲（ProLine=wchar_t[81]，PRO_LINE_SIZE 含 NUL）。供 popup label/help 注入用。
    /// **保留理由**: buffer = wchar_t[81] 真 out buffer,Generated 缺产物(generator 当前
    /// 形态识别覆盖不到 wchar_t[81] out buffer + LPStr key 混合)。</summary>
    [DllImport(Dll, EntryPoint = "ProMessageToBuffer", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMessageToBuffer(
        [Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 81, ArraySubType = UnmanagedType.U2)] char[] buffer,
        [MarshalAs(UnmanagedType.LPWStr)] string msgFile,
        [MarshalAs(UnmanagedType.LPStr)] string msgKey);
}
