using CreoToolkit.Interop;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProCableLocationsOnSegEndGet(pro_model_item cable, out IntPtr p_boundries)
    // 读回每段 cable 末端两点位置对(ProCablelocationPair=`ProCablelocation[2]`,元素为
    // struct pro_model_item)。
    // **所有权 unknown(保守 borrowed)**:头文件(ProCabling.h:1889)与 tkuse 手册(81157)
    // 均只写 "returned as a ProArray" 无 free 句(同文件邻近函数全都明写 ProArrayFree,
    // 此函数独缺)。Bridge 只暴露 boundary 数量 + 每个 pair 的两个 model item 数据快照,
    // **不外露 raw ProArray 句柄**,禁自动 ProArrayFree 直至 PTC 澄清或真机验证。
    // 若真为 owned 而不 free → 泄漏;若真为 borrowed 而 free → 崩溃。此保守语义换稳定性。

    private const int CABLE_BOUNDARY_ITEMS_PER_PAIR = 2;
    private int _cableBoundaryUnknownOwnershipReads;

    /// <summary>读取 cable 每段末端两点位置对。cable 不存在返 null;空 boundary 返空列表。
    /// 每 pair = ProCablelocation[2]=`struct pro_model_item[2]`,SDK 快照拆成两 CableBoundaryItem。
    /// 注意:头文件无 free 契约,unknown 保守 borrowed;raw 句柄不外露,泄漏一次可接受(极小),
    /// PTC 澄清后转 owned 再登记 ProArrayFree。</summary>
    public IReadOnlyList<CableSegmentBoundary>? CableLocationsOnSegEndGet(ItemRef cable)
    {
        if (!TryResolveMdl(cable.Model, out _)) return null;
        var cableItem = new GNS.pro_model_item
        {
            type = (GNS.pro_obj_types)(int)cable.Type,
            id = cable.Id,
        };

        var rc = G.ProCableLocationsOnSegEndGet(cableItem, out IntPtr rawBoundaries);
        if (IsNotFound(rc)) return null;
        Check.Eval(nameof(G.ProCableLocationsOnSegEndGet), rc);

        var boundaries = new List<CableSegmentBoundary>();
        if (rawBoundaries == IntPtr.Zero) return boundaries;

        // BORROWED 保守:只读元素数与内容拷贝,不 ProArrayFree。若真是 owned 会有泄漏,
        // 换稳定性;PTC 澄清后转 owned 再加 finally 释放。SizeGet 失败必须按统一
        // ProErrorPolicy 抛出，不能把坏句柄/线程错误伪装成合法空数组。
        var count = ProArrayMarshal.CountChecked(rawBoundaries);
        if (count == 0) return boundaries;

        var ownershipRead = Interlocked.Increment(ref _cableBoundaryUnknownOwnershipReads);
        if (CablingBoundaryOwnershipPolicy.ShouldWarn(ownershipRead))
        {
            CreoSdkLog.Warn(
                "cabling",
                "seg-end.ownership-unknown",
                "ProCableLocationsOnSegEndGet 返回非空 ProArray 但 free 契约无官方文档；raw 句柄刻意保留不释放。",
                new
                {
                    cable = cable.Id,
                    callCount = ownershipRead,
                    count,
                    ownership = "unknown-borrowed-protection",
                });
        }

        var itemStride = System.Runtime.InteropServices.Marshal.SizeOf<GNS.pro_model_item>();
        var pairStride = itemStride * CABLE_BOUNDARY_ITEMS_PER_PAIR;
        for (int i = 0; i < count; i++)
        {
            var start = System.Runtime.InteropServices.Marshal.PtrToStructure<GNS.pro_model_item>(
                rawBoundaries + i * pairStride);
            var end = System.Runtime.InteropServices.Marshal.PtrToStructure<GNS.pro_model_item>(
                rawBoundaries + i * pairStride + itemStride);
            boundaries.Add(new CableSegmentBoundary(
                new CableBoundaryItem((int)start.type, start.id),
                new CableBoundaryItem((int)end.type, end.id)));
        }

        CreoSdkLog.Trace("cabling", "seg-end.get.bridge",
            new { cable = cable.Id, count = boundaries.Count, ownership = "borrowed-unknown" });
        return boundaries;
    }
}

// CableSegmentBoundary/CableBoundaryItem 已迁到 Model/CableSegmentBoundary.cs(public)。
