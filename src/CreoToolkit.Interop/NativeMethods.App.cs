using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 命令桥单一 managed dispatch 委托：Creo 命令回调最终经 native 蹦床转调它，用 token 路由到具体命令。
/// 返回 0=ok；非 0 由 C# 侧记录，native 蹦床仍对 Creo 返回 0（Creo 忽略返回值）。
/// </summary>
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate int CtkManagedCommandDispatch(int token);

/// <summary>
/// 命令桥 P/Invoke 面（<c>ctk_app.h</c>）。延续 <see cref="NativeMethods"/> 的 cdecl + ExactSpelling 约定。
/// <para>
/// **退役注**：原 4 个 <c>Ctk_MessageDisplay / Ctk_CommandDesignate / Ctk_RibbonDefinitionfileLoad
/// / Ctk_MenubarPushbuttonAdd</c> 已物删，C# 端切直绑 Pro* —— 见 <see cref="NativeMethods.ProApp.cs"/>。
/// 仅保留必须 native 的 5 项：BridgeInitialize/Reserve/Terminate(管理进程级 route identity)、
/// CommandActionAdd(注册 ProCmdActionAdd + SEH trampoline)、LastCommandStatusGet(读 trampoline 写入的
/// status side-channel；Creo 忽略 trampoline 返回值故必须 native 兜底)。
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>安装单一 dispatch 函数指针（C# 钉住后传入）。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_CommandBridgeInitialize", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int Ctk_CommandBridgeInitialize(nint dispatch);

    /// <summary>在任何 <c>ProCmdActionAdd</c> 前一次性预分配完整 route 容量。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_CommandBridgeReserve", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int Ctk_CommandBridgeReserve(int commandCount);

    /// <summary>使 dispatch inert；命令 route identity 保留到进程结束。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_CommandBridgeTerminate", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int Ctk_CommandBridgeTerminate();

    /// <summary>注册命令；内部建立 cmd_id→token 映射。actionName 为 ASCII；priority 5=uiProe2ndImmediate。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_CommandActionAdd", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int Ctk_CommandActionAdd(
        [MarshalAs(UnmanagedType.LPStr)] string actionName, int token, int priority,
        int allowNonActive, int allowAccessory, out nint outCmdId);

    /// <summary>取最近一次回调的 token 与 dispatch 返回码（门用可观测）。无回调发生 outToken=-1。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_LastCommandStatusGet", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int Ctk_LastCommandStatusGet(out int outToken, out int outStatus);

    // 历史 Ctk_MessageDisplay / Ctk_CommandDesignate / Ctk_RibbonDefinitionfileLoad / Ctk_MenubarPushbuttonAdd
    // 已退役改 C# 直绑 Pro*（见 NativeMethods.ProApp.cs）；兜底语义（null helpKey→labelKey 等）
    // 由 C# 包装层 NativeCommandBridge 承担。
}
