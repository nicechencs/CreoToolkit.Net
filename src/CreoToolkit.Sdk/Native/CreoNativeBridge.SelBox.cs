using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProSeldatSelboxesGet(IntPtr sel_data, out IntPtr p_selbox_arr, out int p_selbox_num)
    // 从 selection data 读回一组 selection box(每个 ProSelbox=`double[2][3]` 两角点)。
    // owned mode:data,ProArrayFree(header ProExtobjSel.h:78 "Use ProArrayFree() to free")。
    // sel_data 是 ProWExtobjdata=IntPtr,业务侧从别处构造。

    private const int SEL_BOX_DOUBLES = 6;    // 2 corners × 3 coords
    private const int SEL_BOX_COORDS_PER_CORNER = 3;

    /// <summary>读取 selection data 内的所有 selection box 角点对。sel_data 无效返 null。
    /// 每个 box = (Corner0[3], Corner1[3]) 对角点。</summary>
    public IReadOnlyList<SelectionBoxData>? SeldatSelboxesGet(IntPtr selData)
    {
        if (selData == IntPtr.Zero) return null;

        var rc = G.ProSeldatSelboxesGet(selData, out var boxArr, out int _);
        using var scope = ProArrayScope.OwnedPlain(boxArr);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProSeldatSelboxesGet), rc);

        var boxes = new List<SelectionBoxData>();
        if (scope.Handle == IntPtr.Zero) return boxes;

        var count = ProArrayMarshal.CountChecked(scope.Handle);
        if (count <= 0) return boxes;

        var stride = SEL_BOX_DOUBLES * sizeof(double);
        for (int i = 0; i < count; i++)
        {
            var raw = new double[SEL_BOX_DOUBLES];
            Marshal.Copy(scope.Handle + i * stride, raw, 0, SEL_BOX_DOUBLES);
            var corner0 = new[] { raw[0], raw[1], raw[2] };
            var corner1 = new[] { raw[3], raw[4], raw[5] };
            boxes.Add(new SelectionBoxData(corner0, corner1));
        }
        CreoSdkLog.Trace("selbox", "get.bridge", new { count = boxes.Count });
        return boxes;
    }
}

/// <summary>Selection box 两角点(每角点 3 坐标)。
/// 顶层"selection data"来自 external object selection API,对角点定义局部立方边界。</summary>
internal sealed record SelectionBoxData(IReadOnlyList<double> Corner0, IReadOnlyList<double> Corner1);
