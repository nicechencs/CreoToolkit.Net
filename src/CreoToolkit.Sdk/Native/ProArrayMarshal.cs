using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

/// <summary>ProArray 句柄的读/写封送样板收敛(内部)。所有读方法对 <see cref="IntPtr.Zero"/> /
/// SizeGet 失败容错为 <c>count=0</c>——保持与手写样板的 fail-soft 惯例一致(读回族 miss 归空)。
/// <para><b>stride 契约</b>:泛型读写默认按 <c>Marshal.SizeOf&lt;T&gt;()</c> 逐元素前进,
/// 但 native 元素大小不总等于逻辑 T 的大小(例:<c>ProDrawingDimensionCreate</c> 的 attach 元素
/// = <c>ProSelection[2]</c> = 2×IntPtr 打包 pair)。这类场景走**显式 stride 重载**,由调用点携真值。</para>
/// <para>写方法均返 <see cref="ProArrayScope"/>(默认 OwnedPlain,即 ProArrayFree 策略):
/// 调用点或走 <c>using</c> 自动释放,或 <see cref="ProArrayScope.Transfer"/> 转交下游 native。</para></summary>
internal static class ProArrayMarshal
{
    // ==================== 读 ====================

    /// <summary>严格读取元素数。空句柄返 0；SizeGet 失败经 Check.Eval 抛稳定 SDK 异常。</summary>
    internal static int CountChecked(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return 0;
        var rc = G.ProArraySizeGet(handle, out var count);
        return NormalizeCountResult(rc, count);
    }

    /// <summary>仅供明确允许 best-effort 的诊断路径；失败返回 false，不伪装成合法空数组。</summary>
    internal static bool TryCount(IntPtr handle, out int count)
    {
        if (handle == IntPtr.Zero)
        {
            count = 0;
            return true;
        }

        var rc = G.ProArraySizeGet(handle, out count);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            count = 0;
            return false;
        }

        if (count < 0)
        {
            count = 0;
            return false;
        }
        return true;
    }

    internal static int NormalizeCountResult(GNS.ProErrors rc, int count)
    {
        Check.Eval(nameof(G.ProArraySizeGet), rc);
        if (count < 0)
            throw new InvalidOperationException($"ProArraySizeGet returned negative count {count}.");
        return count;
    }

    /// <summary>按 <c>Marshal.SizeOf&lt;T&gt;()</c> 步长逐元素读回 struct 数组。
    /// 空(count=0)返 <see cref="Array.Empty{T}"/>。</summary>
    internal static T[] ReadStructs<T>(IntPtr handle) where T : unmanaged
        => ReadStructs<T>(handle, Marshal.SizeOf<T>());

    /// <summary>显式 stride 版:处理"逻辑 T 大小 ≠ native 元素大小"场景
    /// (如 pair-packed / padded / union)。</summary>
    internal static T[] ReadStructs<T>(IntPtr handle, int strideBytes) where T : unmanaged
    {
        if (strideBytes < Marshal.SizeOf<T>())
            throw new ArgumentOutOfRangeException(nameof(strideBytes), strideBytes,
                $"strideBytes must be at least sizeof({typeof(T).Name})={Marshal.SizeOf<T>()}.");
        var count = CountChecked(handle);
        if (count == 0) return Array.Empty<T>();
        var result = new T[count];
        for (int i = 0; i < count; i++)
            result[i] = Marshal.PtrToStructure<T>(handle + i * strideBytes);
        return result;
    }

    /// <summary>元素=<see cref="IntPtr"/> 的数组(如 ProArray of ProXxx 不透明句柄)。
    /// stride=<see cref="IntPtr.Size"/>,逐个 <see cref="Marshal.ReadIntPtr(IntPtr, int)"/>。</summary>
    internal static IntPtr[] ReadPointers(IntPtr handle)
    {
        var count = CountChecked(handle);
        if (count == 0) return Array.Empty<IntPtr>();
        var result = new IntPtr[count];
        for (int i = 0; i < count; i++)
            result[i] = Marshal.ReadIntPtr(handle, i * IntPtr.Size);
        return result;
    }

    /// <summary>元素=定长 wchar 缓冲(<c>wchar_t[N]</c> 内联,如 <c>ProName</c>=<c>wchar_t[32]</c>、
    /// <c>ProPath</c>=<c>wchar_t[260]</c>)。<paramref name="elemBytes"/> 为每元素字节数(含 null 位)。
    /// 逐元素 <see cref="Marshal.PtrToStringUni(IntPtr, int)"/> 后截 null 终止子串。</summary>
    internal static string[] ReadFixedWStrings(IntPtr handle, int elemBytes)
        => ReadFixedWStrings(handle, elemBytes, CountChecked(handle));

    /// <summary>显式 count 版:调用点已自行 SizeGet(如严格 Check.Eval 语义)时传入
    /// 已校验的 count,避免二次 SizeGet。</summary>
    internal static string[] ReadFixedWStrings(IntPtr handle, int elemBytes, int count)
    {
        if (elemBytes <= 0 || (elemBytes & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(elemBytes), elemBytes,
                "A fixed UTF-16 element size must be positive and divisible by two.");
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "count cannot be negative.");
        if (handle == IntPtr.Zero && count > 0)
            throw new ArgumentException("A null ProArray handle cannot carry a positive count.", nameof(handle));
        if (count == 0) return Array.Empty<string>();
        // elemBytes 为字节数,单字符占 2 字节(UTF-16)
        var charCount = elemBytes / 2;
        var result = new string[count];
        for (int i = 0; i < count; i++)
        {
            var s = Marshal.PtrToStringUni(handle + i * elemBytes, charCount) ?? string.Empty;
            var z = s.IndexOf('\0');
            result[i] = z >= 0 ? s.Substring(0, z) : s;
        }
        return result;
    }

    // ==================== 写 ====================

    /// <summary>按 <c>Marshal.SizeOf&lt;T&gt;()</c> 步长分配并拷入 struct 数组。
    /// 空 span 返空 owned scope(count=0 的 ProArray)。</summary>
    internal static ProArrayScope AllocStructs<T>(ReadOnlySpan<T> data) where T : unmanaged
        => AllocStructs(data, Marshal.SizeOf<T>());

    /// <summary>显式 stride 版:同 <see cref="ReadStructs{T}(IntPtr, int)"/> 场景。
    /// <paramref name="strideBytes"/> 为 native 元素步长(可以大于 <c>sizeof(T)</c>)。
    /// Alloc 失败经 Check.Eval 抛出(与直调 ProArrayAlloc + Check.Eval 同语义,不吞 rc)。</summary>
    internal static ProArrayScope AllocStructs<T>(ReadOnlySpan<T> data, int strideBytes) where T : unmanaged
    {
        if (strideBytes < Marshal.SizeOf<T>())
            throw new ArgumentOutOfRangeException(nameof(strideBytes), strideBytes,
                $"strideBytes must be at least sizeof({typeof(T).Name})={Marshal.SizeOf<T>()}.");
        var arr = AllocProArrayChecked(data.Length, strideBytes);
        for (int i = 0; i < data.Length; i++)
        {
            var item = data[i];
            // stride 可能 > sizeof(T),两者不等时 T 逻辑体只占前 sizeof(T) 字节,尾部由 native 语义定义
            Marshal.StructureToPtr(item, arr + i * strideBytes, fDeleteOld: false);
        }
        return ProArrayScope.OwnedPlain(arr);
    }

    /// <summary>元素=<see cref="IntPtr"/> 的数组分配(如 wstring* 数组)。
    /// Alloc 失败经 Check.Eval 抛出(与直调 ProArrayAlloc + Check.Eval 同语义,不吞 rc)。</summary>
    internal static ProArrayScope AllocPointers(ReadOnlySpan<IntPtr> data)
    {
        var arr = AllocProArrayChecked(data.Length, IntPtr.Size);
        for (int i = 0; i < data.Length; i++)
            Marshal.WriteIntPtr(arr, i * IntPtr.Size, data[i]);
        return ProArrayScope.OwnedPlain(arr);
    }

    /// <summary>Double 元素数组,stride 语义与 Drawing 的样条族一致:
    /// <paramref name="stride"/>=1(标量 double,如 spline params)或 3(<c>Pro3dPnt</c>,如 points/tangents)。
    /// element_size = <c>sizeof(double) * stride</c>,元素数 = <c>data.Length / stride</c>。
    /// 失败(<c>ProArrayAlloc</c> rc != NO_ERROR 或返 Zero)返 <c>Handle=Zero</c> 的 owned scope,
    /// 调用点凭 <c>Handle==Zero</c> 判失败并走 OOM 合成分支(返 null + trace 的对外契约);
    /// 特意不抛——该调用面的对外异常语义即"失败不抛",与 AllocStructs/AllocPointers
    /// (调用面原为 Check.Eval 抛)策略不同。</summary>
    internal static ProArrayScope AllocDoubles(double[] data, int stride)
    {
        ThrowUtil.IfNull(data);
        if (stride <= 0) throw new ArgumentOutOfRangeException(nameof(stride));
        if (data.Length % stride != 0)
            throw new ArgumentException(
                $"data 长度 {data.Length} 不能被 stride {stride} 整除", nameof(data));
        var count = data.Length / stride;
        var rc = G.ProArrayAlloc(count, sizeof(double) * stride, 1, out var arr);
        if (rc != GNS.ProErrors.PRO_TK_NO_ERROR || arr == IntPtr.Zero)
            return ProArrayScope.OwnedPlain(IntPtr.Zero);
        Marshal.Copy(data, 0, arr, data.Length);
        return ProArrayScope.OwnedPlain(arr);
    }

    // ==================== 内部工具 ====================

    /// <summary>ProArrayAlloc 单点封装:失败经 Check.Eval 抛出(CreoException 族,携函数名+rc,
    /// 不吞真实错误码)。realloc chunk 固定 1(对齐仓内所有现存调用点惯例)。</summary>
    private static IntPtr AllocProArrayChecked(int nObjs, int objSize)
    {
        Check.Eval(nameof(G.ProArrayAlloc), G.ProArrayAlloc(nObjs, objSize, 1, out var arr));
        return arr;
    }
}
