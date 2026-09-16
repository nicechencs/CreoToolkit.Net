using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProEdgedataGet(ref pro_edge_data, out int p_edge_id, [MarshalAs] int[2] edge_surf_ids,
    //                [MarshalAs] ProEdgeDir[2] edge_directions,
    //                out IntPtr p_edge_uv_point_arr, IntPtr, IntPtr)
    // 拆解 pro_edge_data 结构,读回基本几何字段(edge id / 两侧 surface id / 两侧方向)。
    // p_edge_uv_point_arr 是 borrowed ProArray of ProUvParam(`double[2]`),归 ProEdgedataFree
    // 嵌套 free 管;**Bridge 不外露此 raw handle**——业务侧需读 uv 点时可拿 UvPointCount 定长
    // 直接用 P/Invoke 侧拷贝,或后续加 owned-copy API。
    // 三个 curve_data 输出用 IntPtr.Zero(业务侧不需要时)。

    private const int EDGE_SURF_COUNT = 2;

    /// <summary>拆解 pro_edge_data 结构,读回边的 id / 两侧 surface id / 两侧方向,
    /// 以及 borrowed uv 点数组的元素数(uv 点内容不外露以防误 free)。</summary>
    public EdgeDataSnapshot? EdgedataGet(GNS.pro_edge_data edgeData)
    {
        var surfIds = new int[EDGE_SURF_COUNT];
        var directions = new GNS.ProEdgeDir[EDGE_SURF_COUNT];
        var rc = G.ProEdgedataGet(ref edgeData, out int edgeId, surfIds, directions,
            out IntPtr uvPtsRaw, IntPtr.Zero, IntPtr.Zero);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProEdgedataGet), rc);

        int uvCount = 0;
        if (uvPtsRaw != IntPtr.Zero)
        {
            // BORROWED:归 ProEdgedataFree 嵌套 free 管,**禁 ProArrayFree**。只查长度不外露句柄。
            var sizeRc = G.ProArraySizeGet(uvPtsRaw, out uvCount);
            if (sizeRc != GNS.ProErrors.PRO_TK_NO_ERROR) uvCount = 0;
        }

        var directionsInt = new int[EDGE_SURF_COUNT];
        for (int i = 0; i < EDGE_SURF_COUNT; i++)
            directionsInt[i] = (int)directions[i];

        CreoSdkLog.Trace("edge", "data.get.bridge",
            new { edgeId, surf0 = surfIds[0], surf1 = surfIds[1], uvCount });

        return new EdgeDataSnapshot(edgeId, surfIds, directionsInt, uvCount);
    }
}

/// <summary>ProEdgedataGet 拆解结果快照。
/// EdgeId = 边 id;SurfaceIds = 两侧 surface id 数组[2];Directions = 两侧方向(ProEdgeDir 枚举 int)。
/// UvPointCount = 归 ProEdgedataFree 嵌套 free 的 borrowed uv 数组长度;raw 句柄不外露以防误 free。</summary>
internal sealed record EdgeDataSnapshot(int EdgeId, IReadOnlyList<int> SurfaceIds,
    IReadOnlyList<int> Directions, int UvPointCount);
