using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ProSnapshotTrfsGet(const ProName snap_name, const ProAsmcomppath *path,
    //                    ProAsmcomppath **path_arr, ProMatrix **trf_arr)
    // 取 snapshot 内每个 component 的 comp_path 与到顶层的变换矩阵。owned mode:data,
    // 双 ProArrayFree(header ProKinDrag.h:325 "call ProArrayFree() to free")。
    // snap_name 是 ProName=`wchar_t[32]` 定长;path 可 NULL 表顶层 snapshot。

    private const int PRO_NAME_MAX_CHARS = 32;
    private const int PRO_MATRIX_ELEMS = 16;

    /// <summary>ProSnapshotCreate:以当前屏幕位置创建顶层装配 snapshot(约束拷贝自 active
    /// snapshot,新建者成为 active)。名字非法/已存在等 native 错误抛 CreoException。</summary>
    public void SnapshotCreate(string snapshotName)
    {
        ThrowUtil.IfNullOrWhiteSpace(snapshotName);
        var nameHandle = PinProName(snapshotName, out _);
        try
        {
            Check.Eval(nameof(G.ProSnapshotCreate),
                G.ProSnapshotCreate(nameHandle.AddrOfPinnedObject()));
        }
        finally
        {
            nameHandle.Free();
        }
        CreoSdkLog.Trace("snapshot", "create.bridge", new { snapshotName });
    }

    /// <summary>ProSnapshotDelete:按名删除顶层装配 snapshot。不存在时静默(NOT_FOUND 吞)。</summary>
    public void SnapshotDelete(string snapshotName)
    {
        ThrowUtil.IfNullOrWhiteSpace(snapshotName);
        var nameHandle = PinProName(snapshotName, out _);
        try
        {
            var rc = G.ProSnapshotDelete(nameHandle.AddrOfPinnedObject(), IntPtr.Zero);
            if (!IsNotFound(rc))
                Check.Eval(nameof(G.ProSnapshotDelete), rc);
        }
        finally
        {
            nameHandle.Free();
        }
        CreoSdkLog.Trace("snapshot", "delete.bridge", new { snapshotName });
    }

    /// <summary>把字符串装填进 pinned 的 ProName=`wchar_t[32]` 定长缓冲(过长截断,末位 '\0')。</summary>
    private static GCHandle PinProName(string s, out char[] buf)
    {
        buf = new char[PRO_NAME_MAX_CHARS];
        var cap = Math.Min(s.Length, PRO_NAME_MAX_CHARS - 1);
        s.CopyTo(0, buf, 0, cap);
        return GCHandle.Alloc(buf, GCHandleType.Pinned);
    }

    /// <summary>读取指定 snapshot 下每个组件的变换矩阵。找不到 snapshot 返 null;
    /// 空 snapshot 返空列表。matrix 展平为 double[16](row-major)。</summary>
    public IReadOnlyList<SnapshotTransform>? SnapshotTrfsGet(string snapshotName)
    {
        if (string.IsNullOrEmpty(snapshotName)) return null;

        GCHandle nameHandle = default;
        ProArrayScope? pathScope = null, trfScope = null;
        try
        {
            nameHandle = PinProName(snapshotName, out _);
            var rc = G.ProSnapshotTrfsGet(nameHandle.AddrOfPinnedObject(), IntPtr.Zero,
                out var pathArr, out var trfArr);
            pathScope = ProArrayScope.OwnedPlain(pathArr);
            trfScope = ProArrayScope.OwnedPlain(trfArr);
            if (IsNotFound(rc)) return null;
            Check.Eval(nameof(G.ProSnapshotTrfsGet), rc);

            var trfs = new List<SnapshotTransform>();
            if (trfScope.Handle == IntPtr.Zero) return trfs;

            var count = ProArrayMarshal.CountChecked(trfScope.Handle);
            if (count <= 0) return trfs;

            // 平行数组防御:pathArr 独立取 size,索引上限取二者 min,防两数组长度不一致时
            // 读越界(契约上应等长,但不依赖未验证的假设)。
            int pathCount = ProArrayMarshal.CountChecked(pathScope.Handle);

            // ProMatrix=`double[4][4]` = 16 * 8B = 128B per element
            var matStride = PRO_MATRIX_ELEMS * sizeof(double);
            // pro_comp_path 结构:owner(IntPtr) + comp_id_table[25]*int + table_num(int)
            // 大小:IntPtr(8) + 25*4 + 4 = 112B(x64)。用 Marshal.SizeOf 稳态计算。
            var pathStride = Marshal.SizeOf<GNS.pro_comp_path>();

            for (int i = 0; i < count; i++)
            {
                var mat = new double[PRO_MATRIX_ELEMS];
                Marshal.Copy(trfScope.Handle + i * matStride, mat, 0, PRO_MATRIX_ELEMS);

                int tableNum = 0;
                var componentIds = Array.Empty<int>();
                if (pathScope.Handle != IntPtr.Zero && i < pathCount)
                {
                    var path = Marshal.PtrToStructure<GNS.pro_comp_path>(pathScope.Handle + i * pathStride);
                    tableNum = path.table_num;
                    if (tableNum > 0)
                    {
                        componentIds = new int[tableNum];
                        unsafe
                        {
                            for (int j = 0; j < tableNum; j++)
                                componentIds[j] = path.comp_id_table[j];
                        }
                    }
                }
                trfs.Add(new SnapshotTransform(tableNum, componentIds, mat));
            }
            CreoSdkLog.Trace("snapshot", "trfs.get.bridge", new { snapshotName, count = trfs.Count });
            return trfs;
        }
        finally
        {
            // 显式 finally 保序:nameHandle → pathArr → trfArr(pinned handle 与 scope 交错,
            // using 表达不了此顺序)。
            if (nameHandle.IsAllocated) nameHandle.Free();
            pathScope?.Dispose();
            trfScope?.Dispose();
        }
    }
}

// SnapshotTransform 已迁到 Model/SnapshotTransform.cs(public)。
