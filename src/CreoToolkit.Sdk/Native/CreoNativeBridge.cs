using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Errors;
// G = Pro* P/Invoke 类(由 gen_bindings.py 自动产出),与 Ctk_* 手写面 CreoToolkit.Interop.NativeMethods 并存。
// 用别名而非 using static:避免 NativeMethods 同名歧义(Ctk_* 在 Interop.NativeMethods、Pro* 在 Interop.Generated.NativeMethods)。
// GNS = Interop.Generated namespace,引用其中独立 enum/struct(ProErrors/ProBooleans/pro_model_item 等)。
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

/// <summary>
/// <see cref="ICreoNative"/> 的真实实现: 把 L3 操作落到 L2(<see cref="NativeMethods"/> P/Invoke +
/// <see cref="CreoSafeHandle"/> 族 + buffer/handle 释放)。建在真实 Creo iter=1000 零泄漏验证的 L1 正式
/// 三态导出之上(见 ctk_ops.cpp)。
/// <para>
/// 三态映射:
/// <list type="bullet">
/// <item>DATA — 经 <c>CtkBuffer</c>/定长记录 copy-out 成托管值后, 立即 <c>Ctk_FreeBuffer</c>(copy-out-free-in)。</item>
/// <item>BORROWED — 选择副本集由 <c>OwnedSelectionsResource</c> 持 owned ProSelection 副本, 每份包成
/// <see cref="CreoSelectionHandle"/>(SafeHandle + finalizer 兜底), 释放经 <c>CreoReleaseGate</c> 走 ProSelectionFree。</item>
/// <item>LIVE — 元素树句柄包成 owning <see cref="CreoElemtreeHandle"/>(unmanaged blob 保存 feature/tree, 走直绑释放)。</item>
/// </list>
/// 状态码硬不变量(ctk_ops.h): 0=ok / &lt;0=透传 ProError(经 <see cref="Check.Eval"/> 分类) / &gt;0=CtkError。
/// 所有方法都假定已在 Creo 主线程(由 L3 经 <c>CreoDispatcher</c> 保证)。
/// </para>
/// </summary>
internal sealed partial class CreoNativeBridge : ICreoNative
{
    private static bool IsNotFound(GNS.ProErrors e)
        => e == GNS.ProErrors.PRO_TK_E_NOT_FOUND
        || e == GNS.ProErrors.PRO_TK_NOT_EXIST;

    // ===================== ProMdl 句柄获取 helpers =====================
    //
    // 语义铁律(ProMdl.h + PTC 官方 "Identifying Models"):
    //   ProMdlnameInit     - 仅从**已加载会话内存**查找句柄(不触磁盘 I/O)。
    //                        模型不在会话 → 返 PRO_TK_E_NOT_FOUND。
    //   ProMdlnameRetrieve - 会话内存查;**命中即返句柄**,**未命中从磁盘 retrieve 加载**到内存,
    //                        然后初始化句柄(不显示、不置当前)。
    //
    // 设计原则:TryResolveMdl 的**职责仅是"拿句柄"**,不是"加载模型"。业务"读属性/做操作"假定模型
    // 已在会话中,用 Init 语义正确;若需"显式加载",走 TryRetrieveMdl(预留)。
    //
    // 为什么之前误用 Retrieve(已纠正):粗看 cache hit 行为一致,但有副作用——
    //   (1) 模型被 Erase 后再调任意 getter,Retrieve 会从磁盘**重新加载**(预期外);
    //   (2) 同名磁盘文件被改动时,Retrieve 路径可能触发版本对账;
    //   (3) 性能上(虽然小)每次 cache 查询+判定,而 Init 是纯内存查找。
    // 业务语义"我要这个模型的句柄"应当**只**走 Init;若需读盘,业务必须显式 Retrieve。

    /// <summary>从**会话内存**取 ItemRef.Model 的 ProMdl 句柄(走 ProMdlnameInit,不触磁盘)。
    /// 模型不在会话(PRO_TK_E_NOT_FOUND)返 false,由调用方按 query→null 契约处理。
    /// 业务"读属性/做操作"应统一走本 helper;**绝不**用作"按名加载"入口。</summary>
    private static bool TryResolveMdl(ItemRef item, out IntPtr mdl)
        => (int)G.ProMdlnameInit(ToProName(item.Model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(item.Model.Type), out mdl) == 0;

    /// <summary>同上,ModelIdentity 入参版本。</summary>
    private static bool TryResolveMdl(ModelIdentity model, out IntPtr mdl)
        => (int)G.ProMdlnameInit(ToProName(model.Name, 180),
            (GNS.ProMdlfileType)ToProMdlType(model.Type), out mdl) == 0;

    /// <summary>**显式从磁盘 retrieve** 模型并取句柄(走 ProMdlnameRetrieve;cache hit 返句柄,miss 读盘加载)。
    /// 仅用于业务明确"我要加载这个模型"的入口(如未来的显式 retrieve API);普通"读属性/做操作"
    /// 一律走 <see cref="TryResolveMdl(ModelIdentity, out nint)"/>。</summary>
    private static bool TryRetrieveMdl(ModelIdentity model, out IntPtr mdl)
        => (int)G.ProMdlnameRetrieve(ToProName(model.Name, 362),
            (GNS.ProMdlfileType)ToProMdlType(model.Type), out mdl) == 0;

    // ---- Visitor 共享辅助 ----

    // ===================== 内部: 状态码 + 类型映射 + 解码 =====================

    /// <summary>
    /// 评估 L1 导出状态码:0 直接返回; &lt;0 透传 ProError 经 <see cref="Check.Eval"/> 分类(真错抛
    /// <see cref="CreoException"/>, 良性条件放行); &gt;0 为 L1 内部 CtkError, 直接抛。
    /// <para><b>与 <see cref="Check.Eval(string, ProError, string?)"/> 的分工</b>:
    /// 当 caller 拿到的是 <c>int</c>(L1 自定义状态码,含 &gt;0 CtkError)走本方法;
    /// 当 caller 拿到的是 <see cref="ProError"/>(纯 Pro* 调用)直接用 <see cref="Check.Eval"/>。</para>
    /// </summary>
    private static void Ensure(string api, int status, string? detail = null)
    {
        if (status == 0)
            return;
        if (status < 0)
        {
            Check.Eval(api, (ProError)status, detail); // 良性(如某些 *Get 的 NOT_FOUND)不抛
            return;
        }
        throw new CreoException(api, ProError.GeneralError,
            $"L1 内部错误 CtkError={status}{(detail is null ? "" : "; " + detail)}");
    }

    // ---- Pro* 直绑 helpers(generator 产出 char[]/POD struct 签名后,把 .NET 友好类型转为 native 缓冲)----

    /// <summary>把 .NET string 装填到定长 wchar 缓冲(ProName=32/ProMdlName=180/ProFamilyMdlName=362 等),
    /// NUL 终止;过长截断到 size-1。返回 char[size] 缓冲。</summary>
    private static char[] ToProName(string? s, int size = 32)
    {
        var buf = new char[size];
        if (string.IsNullOrEmpty(s)) return buf;
        int copyLen = Math.Min(s.Length, size - 1);
        s.AsSpan(0, copyLen).CopyTo(buf);
        return buf;
    }

    /// <summary>把 char[] wchar 缓冲(由 generator 出参 LPArray char[] 填充)读为 .NET string(NUL 截断)。</summary>
    private static string FromProName(char[] buf)
    {
        int len = Array.IndexOf(buf, '\0');
        return len < 0 ? new string(buf) : new string(buf, 0, len);
    }

    /// <summary>同上,但缓冲是 ushort[](pro_material.matl_name 等 struct 字段用 ByValArray ushort)。</summary>
    private static string FromProNameUshort(ushort[] buf)
    {
        int len = Array.IndexOf(buf, (ushort)0);
        if (len < 0) len = buf.Length;
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = (char)buf[i];
        return new string(chars);
    }

    // CreoModelType ↔ ProMdlType(非连续,不可直接强转);真源=生成枚举 GNS.ProMdlType/GNS.ProMdlfileType,
    // 两枚举在本表涉及的值(1/2/4/19/33/116/121)上一一对应,调用点强转安全。
    // Layout 与 Notebook 在 PTC 语义下同指 .lay 文件类型(头文件注释 "PRO_MDL_LAYOUT: *.lay Notebook model"),
    // 因此二者都映射到 PRO_MDL_LAYOUT=19,反向只归 Notebook。Harness 在 Creo 中并非独立 ProMdlType 成员
    // (制造 harness 是 ProMdlsubtype 子类型),此处显式拒收。
#pragma warning disable CS0618 // 内部映射需引用保留兼容成员 Layout/Harness
    // internal 而非 private:供 Sdk.Tests 直接锁具体真值(见 InternalsVisibleTo)。
    internal static int ToProMdlType(CreoModelType type) => type switch
    {
        CreoModelType.Assembly => (int)GNS.ProMdlType.PRO_MDL_ASSEMBLY,
        CreoModelType.Part     => (int)GNS.ProMdlType.PRO_MDL_PART,
        CreoModelType.Drawing  => (int)GNS.ProMdlType.PRO_MDL_DRAWING,
        CreoModelType.Notebook => (int)GNS.ProMdlType.PRO_MDL_LAYOUT,
        CreoModelType.Layout   => (int)GNS.ProMdlType.PRO_MDL_LAYOUT,   // 与 Notebook 同指 .lay
        CreoModelType.Format   => (int)GNS.ProMdlType.PRO_MDL_DWGFORM,
        CreoModelType.Markup   => (int)GNS.ProMdlType.PRO_MDL_MARKUP,
        CreoModelType.Diagram  => (int)GNS.ProMdlType.PRO_MDL_DIAGRAM,
        CreoModelType.Harness  => throw new NotSupportedException(
            "Creo 无 HARNESS 模型类型;制造 harness 是 ProMdlsubtype 子类型,不是 ProMdlType。"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知 CreoModelType。"),
    };

    internal static CreoModelType FromProMdlType(int t)
    {
        // 反向表以 GNS.ProMdlType 值为真源,只回落 SDK 已声明的成员;
        // 未识别真值(含 3DSECTION=7/2DSECTION=11/MFG=37/REPORT=105 等本批未扩面的枚举)统一回 Unknown。
        if (t == (int)GNS.ProMdlType.PRO_MDL_ASSEMBLY) return CreoModelType.Assembly;
        if (t == (int)GNS.ProMdlType.PRO_MDL_PART)     return CreoModelType.Part;
        if (t == (int)GNS.ProMdlType.PRO_MDL_DRAWING)  return CreoModelType.Drawing;
        if (t == (int)GNS.ProMdlType.PRO_MDL_LAYOUT)   return CreoModelType.Notebook; // 19: Layout/Notebook 同值,归 Notebook
        if (t == (int)GNS.ProMdlType.PRO_MDL_DWGFORM)  return CreoModelType.Format;
        if (t == (int)GNS.ProMdlType.PRO_MDL_MARKUP)   return CreoModelType.Markup;
        if (t == (int)GNS.ProMdlType.PRO_MDL_DIAGRAM)  return CreoModelType.Diagram;
        return CreoModelType.Unknown;
    }
#pragma warning restore CS0618

    private static CreoLayerDisplayStatus ToLayerDisplayStatus(int status) => status switch
    {
        -1 => CreoLayerDisplayStatus.None,
        1 => CreoLayerDisplayStatus.Normal,
        2 => CreoLayerDisplayStatus.Display,
        3 => CreoLayerDisplayStatus.Blank,
        5 => CreoLayerDisplayStatus.Hidden,
        6 => CreoLayerDisplayStatus.Skip,
        _ => throw new CreoException("Ctk_LayerDisplayStatusGet", ProError.InvalidType,
            $"未知 ProLayerDisplay={status}。"),
    };

    private static INativeResource UnwrapResource(INativeResource resource)
    {
        while (resource is IWrappedNativeResource wrapped)
            resource = wrapped.Inner;
        return resource;
    }

    private static (string? Prefix, int Kind) SplitQuery(ParameterQuery? query)
    {
        if (query is not { } q)
            return (null, -1);
        int kind = q.Kind switch
        {
            CreoParamValueKind.String => CtkAbiConstants.ParamString,
            CreoParamValueKind.Double => CtkAbiConstants.ParamDouble,
            CreoParamValueKind.Int => CtkAbiConstants.ParamInt,
            CreoParamValueKind.Bool => CtkAbiConstants.ParamBool,
            _ => -1, // null / Unset: 任意类型
        };
        return (q.NamePrefix, kind);
    }

    // ValidateBuffer / ReadWString / ReadNames / ReadInt32s / ReadDoubles 已物删:
    // 所有 Ctk_* DATA 出参经退役切到 Pro* 直绑(经 Generated/Marshal 解码),这 5 个 SDK 私有 reader
    // 不再有 caller。后续若有新 DATA 三态导出需要 CtkBuffer reader,请加在 Interop/Marshal 域(带单测),
    // 而非把硬编码 api 名(原 ReadWString 残留 "Ctk_ModelCurrent" 已退役)的腐化样板搬回。
    // 设计意图:Sdk.Native 只负责 L3 业务封装,buffer 解码属于 Interop 层职责。

    /// <summary>
    /// <see cref="INativeResource"/> 的真实实现: 包一个 owning <see cref="CreoSafeHandle"/>。
    /// <see cref="IDisposable.Dispose"/> 触发 SafeHandle 释放 → 经 <c>CreoReleaseGate</c> 在主线程直释
    /// 或跨线程入队(由句柄自身的 <c>FreeKind</c> 决定走 CreoToolkit_Free / ProFeatureElemtreeFree)。
    /// </summary>
    internal sealed class NativeHandleResource : INativeResource
    {
        private readonly CreoSafeHandle _handle;

        public NativeHandleResource(CreoSafeHandle handle) => _handle = handle;

        public bool IsDisposed => _handle.IsClosed;

        /// <summary>底层原始句柄(仅供仍存活时的动作型调用, 如高亮)。</summary>
        public nint RawHandle => _handle.DangerousGetHandle();

        public void Dispose() => _handle.Dispose();
    }
}
