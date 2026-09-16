using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- 几何评估 TestMeasure (ProGeomitemAngle/Distance/DiameterEval) ----
    // 直绑头文件优先:不扩 native wrapper;G.ProSelectionAlloc/Free 临时包 ItemRef → ProSelection*
    // → G.ProGeomitem*Eval 求值 → ProSelectionFree 释放。
    // pro_comp_path 顶层零件: owner=mdl, table_num=0 (无组件路径)。
    // NotFound/分配失败 → null;真错(类型不匹配等)Check.Eval 抛 ProToolkitException。

    /// <summary>测两个几何项之间的角度(度);NotFound/跨模型 → null;
    /// 对应 TestMeasure.c USER_ANGLE_EVAL(2 个 edge/线性几何项)。</summary>
    public double? GeomitemAngleEval(ItemRef item1, ItemRef item2)
    {
        if (item1.Model != item2.Model) return null;
        if (!TryResolveMdl(item1.Model, out var mdl)) return null;
        IntPtr sel1 = IntPtr.Zero, sel2 = IntPtr.Zero;
        try
        {
            if (!TryAllocSelection(mdl, item1, out sel1)) return null;
            if (!TryAllocSelection(mdl, item2, out sel2)) return null;
            var rc = G.ProGeomitemAngleEval(sel1, sel2, out var value);
            if (IsNotFound(rc)) return null;
            Check.Eval(nameof(G.ProGeomitemAngleEval), rc);
            return value;
        }
        finally
        {
            if (sel1 != IntPtr.Zero) G.ProSelectionFree(ref sel1);
            if (sel2 != IntPtr.Zero) G.ProSelectionFree(ref sel2);
        }
    }

    /// <summary>测两个几何项之间的距离(模型长度单位);NotFound/跨模型 → null;
    /// 对应 TestMeasure.c USER_DISTANCE_EVAL(surface/point/axis 任意 2 个)。</summary>
    public double? GeomitemDistanceEval(ItemRef item1, ItemRef item2)
    {
        if (item1.Model != item2.Model) return null;
        if (!TryResolveMdl(item1.Model, out var mdl)) return null;
        IntPtr sel1 = IntPtr.Zero, sel2 = IntPtr.Zero;
        try
        {
            if (!TryAllocSelection(mdl, item1, out sel1)) return null;
            if (!TryAllocSelection(mdl, item2, out sel2)) return null;
            var rc = G.ProGeomitemDistanceEval(sel1, sel2, out var value);
            if (IsNotFound(rc)) return null;
            Check.Eval(nameof(G.ProGeomitemDistanceEval), rc);
            return value;
        }
        finally
        {
            if (sel1 != IntPtr.Zero) G.ProSelectionFree(ref sel1);
            if (sel2 != IntPtr.Zero) G.ProSelectionFree(ref sel2);
        }
    }

    /// <summary>测单个 surface 的直径(仅圆柱/圆锥面有效);NotFound → null;
    /// 对应 TestMeasure.c USER_DIAMETER_EVAL(单个 surface)。</summary>
    public double? GeomitemDiameterEval(ItemRef item)
    {
        if (!TryResolveMdl(item.Model, out var mdl)) return null;
        IntPtr sel = IntPtr.Zero;
        try
        {
            if (!TryAllocSelection(mdl, item, out sel)) return null;
            var rc = G.ProGeomitemDiameterEval(sel, out var value);
            if (IsNotFound(rc)) return null;
            Check.Eval(nameof(G.ProGeomitemDiameterEval), rc);
            return value;
        }
        finally
        {
            if (sel != IntPtr.Zero) G.ProSelectionFree(ref sel);
        }
    }

    /// <summary>临时 ProSelection 分配 helper:顶层零件场景(owner=mdl, table_num=0);
    /// NotFound → 静默 false,真错抛 <see cref="ProToolkitException"/>。
    /// pro_comp_path 含 fixed int[25] 字段 → unsafe context 必需。</summary>
    private static unsafe bool TryAllocSelection(IntPtr mdl, ItemRef item, out IntPtr sel)
    {
        var path = default(GNS.pro_comp_path);
        path.owner = mdl;
        path.table_num = 0;
        var mi = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)item.Type,
            id = item.Id,
            owner = mdl,
        };
        var rc = G.ProSelectionAlloc(ref path, ref mi, out sel);
        if (rc == GNS.ProErrors.PRO_TK_NO_ERROR) return true;
        if (IsNotFound(rc)) { sel = IntPtr.Zero; return false; }
        Check.Eval(nameof(G.ProSelectionAlloc), rc);
        sel = IntPtr.Zero;
        return false;
    }
}
