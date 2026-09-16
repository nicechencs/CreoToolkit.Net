using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 手写 P/Invoke 面：仅绑 L1 自有 C ABI（<c>ctk_api.h</c> + <c>ctk_ops.h</c>，<c>CreoToolkit.NativeAbi</c> 库名）。
/// 全部 cdecl + <see cref="DllImportAttribute.ExactSpelling"/>；宽串入参显式 LPWStr；脱 Creo 不加载（单测经
/// 注入接缝 fake <c>ICreoNative</c>/<c>CreoReleaseGate</c>，不触发真实 P/Invoke）。
/// <para>
/// 状态码硬不变量（<c>ctk_ops.h</c>）：<c>int32</c>，0=ok / &lt;0=透传 ProError / &gt;0=CtkError。
/// </para>
/// <para>
/// 历史背景：历史重构阶段原本大量 <c>Ctk_*</c> wrapper 走过本面，后续随
/// generator 解锁全部经 Pro* 直绑退役并物删；保留项只剩下面这几个 ABI 必需面。
/// 防再写 wrapper 的准入门见 L1 准入门；<b>Pro* 直绑请加在
/// <c>CreoToolkit.Interop.Generated.NativeMethods</c>（生成器产出），不要混入本类。</b>
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    private const string Dll = "CreoToolkit.NativeHost.dll";
    private const CallingConvention Cc = CallingConvention.Cdecl;

    /// <summary>按 <c>CtkOwnedBlock</c> 描述符释放（按 kind 分派；含 <c>PROARRAY_OF_HANDLE</c> 逐元素 free）。NULL 安全。</summary>
    [DllImport(Dll, EntryPoint = "CreoToolkit_Free", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern void CreoToolkit_Free(nint handle);

    /// <summary>
    /// 释放 DATA 值信封的数据块（纯 CRT，任意线程可调）；只释 <c>b-&gt;data</c> 并清零字段。
    /// native 真签名是 <c>Ctk_FreeBuffer(CtkBuffer*)</c>——只此一个按结构指针的绑定；<b>绝不</b>提供裸指针
    /// 重载（否则把 <c>char*/wchar_t*</c> 当 <c>CtkBuffer*</c> 解读会读出伪 data/count/elem_size，有堆破坏风险）。
    /// </summary>
    [DllImport(Dll, EntryPoint = "Ctk_FreeBuffer", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern void Ctk_FreeBuffer(ref CtkBuffer buffer);

    /// <summary>回填 ABI 不变量（纯值，无 Creo 调用，任意线程）。见 <see cref="CtkAbi.Verify"/>。</summary>
    [DllImport(Dll, EntryPoint = "Ctk_GetAbiInfo", ExactSpelling = true, CallingConvention = Cc)]
    internal static extern void Ctk_GetAbiInfo(out CtkAbiInfo info);
}
