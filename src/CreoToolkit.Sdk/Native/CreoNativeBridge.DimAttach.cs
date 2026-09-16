using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProDimensionAttachmentsGet / ProDrawingDimAttachpointsGet / ProGtolAttachMakeDimGet
    // 三 heap-out ProArray-of-ProDimAttachment 家族。attachments_arr 元素是 ProSelection[2]
    // (定长 2 元 pair,intersection type);ProDimSense 是 POD 4 字段 struct。
    // 释放:attachments_arr → ProDimattachmentarrayFree(专用嵌套 free 释放数组内 ProSelection);
    //       dsense_arr → ProArrayFree(default)——同函数双出参异 free 策略。
    // 本层不暴露 raw ProSelection handle(生命周期归 ProDimattachmentarrayFree,不能长期持有);
    // 需要 handle 请在同一 session.Run 内消费,或后续加 ProSelectionCopy 副本机制。
    //
    // null 语义(ProDimension.h:1652 + 真机实证):此 API 只对 reference/driven 尺寸有效,
    // driving(普通)尺寸命中 PRO_TK_INVALID_ITEM/BAD_INPUTS 是设计语义非异常场景,
    // 与 Asmcomppath 家族 IsAsmcompPathMiss 同款归 null(query 契约:不适用=null)。

    private static bool IsDimAttachMiss(GNS.ProErrors rc)
        => IsNotFound(rc)
        || rc == GNS.ProErrors.PRO_TK_BAD_INPUTS
        || rc == GNS.ProErrors.PRO_TK_INVALID_ITEM;

    /// <summary>attachments_arr 专用嵌套 free 委托:ProDimattachmentarrayFree 会递归释放
    /// 数组内每个 ProSelection[2] pair,禁降级到 default ProArrayFree(会遗漏元素)。
    /// 按值传数组基指针(ProDimension.h:1680 + 官方样例 TestDimension.c:793 双证)。
    /// free 失败不抛,同 ProArrayScope.PlainArrayFree 先例——记 Warn 后保守泄漏。</summary>
    private static readonly Action<IntPtr> DimAttachmentArrayFreeAction = ptr =>
    {
        var rc = G.ProDimattachmentarrayFree(ptr);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
            CreoSdkLog.Warn("dim-attach", "free.failed",
                "ProDimattachmentarrayFree 失败，句柄不再重试释放（保守泄漏换稳定）",
                new { rc = (int)rc, handle = ptr.ToInt64() });
    };

    /// <summary>ProDimensionAttachmentsGet 读回 3D 尺寸的附着信息(annotation plane+senses+orient)。
    /// 尺寸不存在 / driving(未 attach)尺寸不适用 → null;attachments 数组返 count 与
    /// senses 详情(handle 不外露)。</summary>
    public DimensionAttachmentsInfo? DimensionAttachmentsGet(ItemRef dimension)
    {
        if (!TryResolveMdl(dimension.Model, out var ownerMdl)) return null;
        // owner 必设:ProDimension 靠 owner+type+id 定位,owner=0 一律 miss(真机实证)
        var dim = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)dimension.Type, id = dimension.Id, owner = ownerMdl };
        var plane = default(GNS.pro_model_item);
        GNS.pro_dim_orient orient = default;
        var rc = G.ProDimensionAttachmentsGet(ref dim, ref plane, out var attachArr, out var senseArr, out orient);
        // using 逆序释放:声明序取释放序(attach 先于 sense)的反向。
        using var senseScope = ProArrayScope.OwnedPlain(senseArr);
        using var attachScope = ProArrayScope.OwnedCustom(attachArr, DimAttachmentArrayFreeAction);
        if (IsDimAttachMiss(rc)) return null;
        Check.Eval(nameof(G.ProDimensionAttachmentsGet), rc);
        return BuildAttachInfo("dim.attach.get",
            planeModel: dimension.Model,
            planeItem: plane,
            orient: (int)orient,
            attachArr: attachScope.Handle,
            senseArr: senseScope.Handle);
    }

    /// <summary>ProDrawingDimAttachpointsGet 读回图纸尺寸的附着信息。
    /// annotation plane 对图纸不适用(该 API 无此出参),返 null AnnotationPlane。</summary>
    public DimensionAttachmentsInfo? DrawingDimensionAttachmentsGet(ModelIdentity drawing, ItemRef dimension)
    {
        if (!TryResolveMdl(drawing, out var mdl)) return null;
        // dim.owner 用 dim 真实归属(drawing 建的 dim 默认存关联 solid);解析失败退回 drawing mdl
        if (!TryResolveMdl(dimension.Model, out var dimOwnerMdl)) dimOwnerMdl = mdl;
        var dim = new GNS.pro_model_item { type = (GNS.pro_obj_types)(int)dimension.Type, id = dimension.Id, owner = dimOwnerMdl };
        var rc = G.ProDrawingDimAttachpointsGet(mdl, ref dim, out var attachArr, out var senseArr);
        // using 逆序释放:声明序取释放序(attach 先于 sense)的反向。
        using var senseScope = ProArrayScope.OwnedPlain(senseArr);
        using var attachScope = ProArrayScope.OwnedCustom(attachArr, DimAttachmentArrayFreeAction);
        if (IsDimAttachMiss(rc)) return null;
        Check.Eval(nameof(G.ProDrawingDimAttachpointsGet), rc);
        return BuildAttachInfo("drawing.dim.attach.get",
            planeModel: null,
            planeItem: default,
            orient: (int)GNS.pro_dim_orient.PRO_DIM_ORNT_NONE,
            attachArr: attachScope.Handle,
            senseArr: senseScope.Handle);
    }

    /// <summary>ProGtolAttachMakeDimGet 读回 GTol 附着的 make-dim 参数(plane+attach+sense+orient+location)。
    /// gtol_attach 由业务侧构造并传入 IntPtr 句柄(见 ProGtolAttach.h)。</summary>
    public DimensionAttachmentsInfo? GtolAttachMakeDimGet(ModelIdentity ownerModel, IntPtr gtolAttach)
    {
        if (gtolAttach == IntPtr.Zero) return null;
        if (!TryResolveMdl(ownerModel, out _)) return null;
        var plane = default(GNS.pro_model_item);
        GNS.pro_dim_orient orient = default;
        var location = new double[3];
        var rc = G.ProGtolAttachMakeDimGet(gtolAttach, ref plane, out var attachArr, out var senseArr, out orient, location);
        // using 逆序释放:声明序取释放序(attach 先于 sense)的反向。
        using var senseScope = ProArrayScope.OwnedPlain(senseArr);
        using var attachScope = ProArrayScope.OwnedCustom(attachArr, DimAttachmentArrayFreeAction);
        if (IsDimAttachMiss(rc)) return null;
        Check.Eval(nameof(G.ProGtolAttachMakeDimGet), rc);
        var info = BuildAttachInfo("gtol.attach.make_dim.get",
            planeModel: ownerModel,
            planeItem: plane,
            orient: (int)orient,
            attachArr: attachScope.Handle,
            senseArr: senseScope.Handle);
        return info with { LocationXyz = (location[0], location[1], location[2]) };
    }

    // ---- 内部 helpers ----

    private DimensionAttachmentsInfo BuildAttachInfo(
        string traceEvent,
        ModelIdentity? planeModel,
        GNS.pro_model_item planeItem,
        int orient,
        IntPtr attachArr,
        IntPtr senseArr)
    {
        int attachCount = 0;
        if (attachArr != IntPtr.Zero)
        {
            var rc = G.ProArraySizeGet(attachArr, out attachCount);
            if (rc != GNS.ProErrors.PRO_TK_NO_ERROR) attachCount = 0;
        }

        var senses = new List<DimSenseInfo>();
        if (senseArr != IntPtr.Zero)
        {
            var rc = G.ProArraySizeGet(senseArr, out int senseCount);
            if (rc == GNS.ProErrors.PRO_TK_NO_ERROR && senseCount > 0)
            {
                var stride = Marshal.SizeOf<GNS.pro_dim_sense>();
                for (int i = 0; i < senseCount; i++)
                {
                    var s = Marshal.PtrToStructure<GNS.pro_dim_sense>(senseArr + i * stride);
                    senses.Add(new DimSenseInfo((int)s.type, s.sense, (int)s.orient_hint));
                }
            }
        }

        ItemRef? planeRef = null;
        if (planeModel is not null && planeItem.id != 0)
            planeRef = new ItemRef(planeModel.Value, (CreoModelItemType)(int)planeItem.type, planeItem.id);

        CreoSdkLog.Trace("dim-attach", traceEvent,
            new { attachCount, senseCount = senses.Count, orient, hasPlane = planeRef.HasValue });

        return new DimensionAttachmentsInfo(planeRef, orient, attachCount, senses, null);
    }
}

/// <summary>3D/2D 尺寸的附着信息读取结果。
/// AttachmentCount = 数组内 pair 数(每 pair=ProSelection[2] intersection type)。
/// 单个 pair 内 handle 未外露(生命周期归 ProDimattachmentarrayFree,不能长期持有);
/// 后续需要具体 handle 时再加 ProSelectionCopy 副本机制。
/// LocationXyz 仅 GtolAttachMakeDimGet 填充。</summary>
internal sealed record DimensionAttachmentsInfo(
    ItemRef? AnnotationPlane,
    int OrientHint,
    int AttachmentCount,
    IReadOnlyList<DimSenseInfo> Senses,
    (double X, double Y, double Z)? LocationXyz);

/// <summary>ProDimSense 4 字段读回值(type/sense/orient_hint 三主字段,angle_sense 12 字节子结构暂略)。</summary>
internal sealed record DimSenseInfo(int Type, int Sense, int OrientHint);
