using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    /// <summary>组合流程:物化 FLAT annotation plane 句柄 → alloc GtolAttach → set FREE 定位 →
    /// 读回 make-dim 信息 → free GtolAttach。业务侧无需处理 IntPtr 生命周期。
    /// 用于在真 GtolCreate 前预览 attach 的 make-dim 信息(annotation plane / attach count /
    /// senses / location)。
    /// <para>注意:内部调用 ProAnnotationplaneFlatToScreenCreate(头文件措辞
    /// "Returns the annotation plane item representing...",是否持久化 plane item
    /// 未经真机验证)——保守起见按可能改模型对待,sample 元数据标 Yellow。</para></summary>
    public DimensionAttachmentsInfo? PrepareGtolAttachAndReadMakeDim(
        ModelIdentity model, double x, double y, double z)
    {
        if (!TryResolveMdl(model, out var mdl)) return null;

        // 1. FLAT annotation plane 句柄(无几何依赖)
        var annotPlane = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProAnnotationplaneFlatToScreenCreate),
            G.ProAnnotationplaneFlatToScreenCreate(mdl, GNS.ProBooleans.PRO_B_FALSE, ref annotPlane));

        // 2-4. alloc 也放 try 内:若 native 写了 attach 又返回错误码,Check.Eval 抛出时
        // finally 仍能配对释放。
        IntPtr attach = IntPtr.Zero;
        try
        {
            Check.Eval(nameof(G.ProGtolAttachAlloc),
                G.ProGtolAttachAlloc(mdl, out attach));

            // 3. Set FREE 定位
            var location = new double[] { x, y, z };
            Check.Eval(nameof(G.ProGtolAttachFreeSet),
                G.ProGtolAttachFreeSet(attach, ref annotPlane, location));

            // 4. 读 make-dim 信息(复用 GtolAttachMakeDimGet 走 IntPtr)
            return GtolAttachMakeDimGet(model, attach);
        }
        finally
        {
            if (attach != IntPtr.Zero)
                G.ProGtolAttachFree(ref attach);
        }
    }

    /// <summary>在模型上创建形位公差(GTol)。
    /// 序列: FlatToScreenCreate → AttachAlloc → AttachFreeSet → MdlGtolCreate → Show(容错)。
    /// attach 在 finally 中配对释放(Create 不消费 attach);
    /// value_str 入参经 Marshal.StringToHGlobalUni 分配,finally 释放。</summary>
    public ItemRef GtolCreate(
        ModelIdentity model,
        CreoGtolType type,
        string valueString,
        double x, double y, double z)
    {
        ThrowUtil.IfNullOrWhiteSpace(valueString);
        if (!Enum.IsDefined(type.GetType(), type))
            throw new ArgumentException($"无效的形位公差类型 {type}", nameof(type));

        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException(
                $"模型 {model.Name} 不在会话中(需先 Retrieve/Load)");

        // 1. 注释平面(flat-to-screen,无几何依赖)
        var annotPlane = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProAnnotationplaneFlatToScreenCreate),
            G.ProAnnotationplaneFlatToScreenCreate(mdl, GNS.ProBooleans.PRO_B_FALSE, ref annotPlane));

        // 2. 附着结构 —— alloc 在 try 内,防"写句柄又返错"泄漏窗口
        IntPtr attach = IntPtr.Zero;
        IntPtr valuePtr = IntPtr.Zero;
        try
        {
            Check.Eval(nameof(G.ProGtolAttachAlloc),
                G.ProGtolAttachAlloc(mdl, out attach));
            // 3. FREE 附着(模型坐标定位)
            var location = new double[] { x, y, z };
            Check.Eval(nameof(G.ProGtolAttachFreeSet),
                G.ProGtolAttachFreeSet(attach, ref annotPlane, location));

            // 4. 创建 GTol
            valuePtr = Marshal.StringToHGlobalUni(valueString);
            var gtol = default(GNS.pro_model_item);
            Check.Eval(nameof(G.ProMdlGtolCreate),
                G.ProMdlGtolCreate(mdl, (GNS.ProGtolType)(int)type, attach, valuePtr, ref gtol));

            // 5. 显示(容错:失败记日志不致命)
            TryShowAnnotation(ref gtol, mdl, model.Name);

            CreoSdkLog.Trace("gtol", "create.ok",
                new { model = model.Name, type = type.ToString(), valueString, id = gtol.id });
            return new ItemRef(model, CreoModelItemType.Gtol, gtol.id);
        }
        finally
        {
            // 配对释放 attach
            if (attach != IntPtr.Zero)
                G.ProGtolAttachFree(ref attach);
            // 释放托管分配的 value_str
            if (valuePtr != IntPtr.Zero)
                Marshal.FreeHGlobal(valuePtr);
        }
    }

    /// <summary>读回形位公差类型和值字符串。
    /// wstring 出参经 ProWstringFree 释放(头注契约)。</summary>
    public GtolInfo GtolRead(ModelIdentity model, int gtolId)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException(
                $"模型 {model.Name} 不在会话中(需先 Retrieve/Load)");

        var gtol = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_GTOL,
            id = gtolId,
            owner = mdl,
        };

        // 类型读取
        Check.Eval(nameof(G.ProGtolTypeGet),
            G.ProGtolTypeGet(ref gtol, out var nativeType));

        // 值字符串读取
        string? valueString = null;
        var vsRc = G.ProGtolValueStringGet(ref gtol, out var valuePtr);
        if (vsRc == GNS.ProErrors.PRO_TK_NO_ERROR && valuePtr != IntPtr.Zero)
        {
            try { valueString = Marshal.PtrToStringUni(valuePtr); }
            finally { G.ProWstringFree(valuePtr); }
        }
        else if (vsRc != GNS.ProErrors.PRO_TK_NO_ERROR
              && vsRc != GNS.ProErrors.PRO_TK_E_NOT_FOUND)
        {
            Check.Eval(nameof(G.ProGtolValueStringGet), vsRc);
        }

        CreoSdkLog.Trace("gtol", "read.ok",
            new { model = model.Name, gtolId, type = (int)nativeType, valueString });
        return new GtolInfo((int)nativeType, valueString);
    }

    /// <summary>删除形位公差。</summary>
    public void GtolDelete(ModelIdentity model, int gtolId)
    {
        if (!TryResolveMdl(model, out var mdl))
            throw new InvalidOperationException(
                $"模型 {model.Name} 不在会话中(需先 Retrieve/Load)");

        var gtol = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_GTOL,
            id = gtolId,
            owner = mdl,
        };

        Check.Eval(nameof(G.ProGtolDelete),
            G.ProGtolDelete(ref gtol));

        CreoSdkLog.Trace("gtol", "delete.ok",
            new { model = model.Name, gtolId });
    }

    /// <summary>ProAnnotationShow 容错调用:comp_path 传默认值,view 传 NULL。
    /// 失败只记日志不抛(设计契约)。</summary>
    private static void TryShowAnnotation(ref GNS.pro_model_item annotation, IntPtr mdl, string modelName)
    {
        try
        {
            var emptyPath = default(GNS.pro_comp_path);
            emptyPath.owner = mdl;
            emptyPath.table_num = 0;
            var rc = G.ProAnnotationShow(ref annotation, ref emptyPath, IntPtr.Zero);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR
                && rc != GNS.ProErrors.PRO_TK_NO_CHANGE)
            {
                CreoSdkLog.Trace("gtol", "show.warn",
                    new { model = modelName, id = annotation.id, rc = (int)rc });
            }
        }
        catch (Exception ex)
        {
            CreoSdkLog.Trace("gtol", "show.error",
                new { model = modelName, id = annotation.id, error = ex.Message });
        }
    }
}
