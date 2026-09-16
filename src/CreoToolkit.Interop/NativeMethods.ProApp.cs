using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 命令/菜单/消息桥手写 P/Invoke 保留区。
/// <para>
/// 历史:退役 Ctk_* 伪 wrapper 时,这里曾有 ProMessageDisplay / ProCmdDesignate /
/// ProRibbonDefinitionfileLoad / ProMenubarMenuAdd / ProMenubarmenuPushbuttonAdd 5 个
/// 手写 wrapper(签名走 LPStr/LPWStr,绕开 Generated [Out, LPArray] marshalling bug)。
/// </para>
/// <para>
/// gen_bindings.py 加 manifest fixed_buffers schema 后,4 个手写已退役
/// (caller 改走 <see cref="Generated.NativeMethods"/> Generated LPStr/LPWStr 签名)。
/// 本文件仅保留 <see cref="ProMessageDisplay"/> 一个 —— variadic + __arglist 形态
/// generator 当前不识别,Generated 缺产物。
/// </para>
/// <para>
/// 后续:若头文件 parser 支持 variadic(__arglist),把 ProMessageDisplay 也迁到 manifest
/// 并退役本文件(连同 NativeMethods.ProMenu.cs 8 个仍保留的 ref uiCmdValue / 复杂 buffer 拼装手写)。
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>显示用户消息。<c>messageName</c> 走 ASCII，文件名/文本走宽串。
    /// 原始签名带 <c>__arglist</c>(C 端 variadic)，本项目固定 3 参传 0 个 vararg——cdecl caller 清栈,
    /// 与传统固定 3 参调用等价(VB API 同形使用)。
    /// **保留理由**: generator 当前不识别 variadic,Generated 缺产物;
    /// 改用 fixed 3 param 形态简化 caller。</summary>
    [DllImport(Dll, EntryPoint = "ProMessageDisplay", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern int ProMessageDisplay(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPStr)] string messageName,
        [MarshalAs(UnmanagedType.LPWStr)] string text);
}
