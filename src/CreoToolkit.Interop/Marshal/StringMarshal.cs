using System.Runtime.InteropServices;

namespace CreoToolkit.Interop;

/// <summary>
/// 按调用粒度处理字符串封送的四类场景，无全局 <c>CharSet</c>，每个 helper 明确指定编码。
///
/// 1. 固定长度输出缓冲区 — native 写入调用方持有的 span（stackalloc/Span）。
/// 2. 输入字符串（LPStr / LPWStr）— 传入托管字符串，调用方选 ANSI 或 UTF-16。
/// 3. 动态返回字符串 — native 分配；托管侧拷出后经 Ctk_FreeBuffer 释放信封。
/// 4. 字符串数组 — native char** 数组；逐元素拷出。
/// </summary>
public static class StringMarshal
{
    // ---- (1) 固定长度输出缓冲区 -------------------------------------------------

    /// <summary>
    /// 从 native 写入的固定缓冲区读取 NUL 结尾字符串。无 NUL 则返回整段 span。
    /// 纯托管 span 路径，热路径无堆分配。
    /// </summary>
    public static string ReadFixedBuffer(ReadOnlySpan<char> buffer)
    {
        int nul = buffer.IndexOf('\0');
        ReadOnlySpan<char> slice = nul >= 0 ? buffer.Slice(0, nul) : buffer;
#if NET472
        // net472 无 string(ReadOnlySpan<char>) 构造函数，经 char[] 中转。
        return slice.IsEmpty ? string.Empty : new string(slice.ToArray());
#else
        return new string(slice);
#endif
    }

    /// <summary>
    /// 将 <paramref name="value"/> 写入调用方固定缓冲区并追加 NUL，返回写入字符数（不含终止符）。
    /// 缓冲区不足时抛异常，与 native 固定缓冲区契约一致。
    /// </summary>
    public static int WriteFixedBuffer(string value, Span<char> buffer)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (value.Length + 1 > buffer.Length)
            throw new ArgumentException(
                $"Buffer of {buffer.Length} chars is too small for a {value.Length}-char value plus NUL terminator.",
                nameof(buffer));

        value.AsSpan().CopyTo(buffer);
        buffer[value.Length] = '\0';
        return value.Length;
    }

    // ---- (2) 输入字符串（LPStr / LPWStr）----------------------------------------------

    /// <summary>托管字符串转 native ANSI（LPStr）缓冲区，调用方用 <see cref="FreeNative"/> 释放。</summary>
    public static nint ToNativeAnsi(string? value)
        => value is null ? IntPtr.Zero : Marshal.StringToHGlobalAnsi(value);

    /// <summary>托管字符串转 native UTF-16（LPWStr）缓冲区，调用方用 <see cref="FreeNative"/> 释放。</summary>
    public static nint ToNativeUtf16(string? value)
        => value is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(value);

    /// <summary>释放 <see cref="ToNativeAnsi"/> / <see cref="ToNativeUtf16"/> 分配的缓冲区。</summary>
    public static void FreeNative(nint ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    // ---- (3) 动态返回字符串 --------------------------------------------------------------

    /// <summary>
    /// 将 native 返回的 NUL 结尾 ANSI 字符串拷入托管字符串，不释放 native 缓冲区（所有权由调用方持有）。
    /// </summary>
    public static string? ReadReturnedAnsi(nint nativeString)
        => nativeString == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(nativeString);

    /// <summary>
    /// 将 native 返回的 NUL 结尾 UTF-16 字符串拷入托管字符串，不释放 native 缓冲区。
    /// </summary>
    public static string? ReadReturnedUtf16(nint nativeString)
        => nativeString == IntPtr.Zero ? null : Marshal.PtrToStringUni(nativeString);

    // L1 ABI 的动态数据一律经 CtkBuffer 信封返回，由 NativeMethods.Ctk_FreeBuffer 释放；
    // 不提供裸指针 Free，防止调用方将裸串指针误传入信封接口导致堆破坏。

    // ---- (4) 字符串数组 -----------------------------------------------------------------

    /// <summary>
    /// 从 native <c>char**</c> 数组拷出 <paramref name="count"/> 个 ANSI 字符串，null 元素保留为 <c>null</c>，不释放 native 内存。
    /// </summary>
    public static string?[] ReadAnsiArray(nint nativeArray, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new string?[count];
        if (nativeArray == IntPtr.Zero || count == 0) return result;

        for (int i = 0; i < count; i++)
        {
            nint elem = Marshal.ReadIntPtr(nativeArray, i * IntPtr.Size);
            result[i] = elem == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(elem);
        }
        return result;
    }

    /// <summary>
    /// 从 native <c>wchar_t**</c> 数组拷出 <paramref name="count"/> 个 UTF-16 字符串，null 元素保留为 <c>null</c>，不释放 native 内存。
    /// </summary>
    public static string?[] ReadUtf16Array(nint nativeArray, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new string?[count];
        if (nativeArray == IntPtr.Zero || count == 0) return result;

        for (int i = 0; i < count; i++)
        {
            nint elem = Marshal.ReadIntPtr(nativeArray, i * IntPtr.Size);
            result[i] = elem == IntPtr.Zero ? null : Marshal.PtrToStringUni(elem);
        }
        return result;
    }
}
