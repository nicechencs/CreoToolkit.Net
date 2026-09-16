using System;
using System.Runtime.InteropServices;

namespace CreoToolkit.Interop.Generated;

internal static partial class NativeMethods
{
    // 手写覆盖: IntPtr.Zero 表示 NULL ProElempath; .g.cs 原 ref path 签名保留。
    [DllImport(Dll, EntryPoint = "ProFeatureElemtreeExtract",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProFeatureElemtreeExtract(
        ref pro_model_item feature,
        IntPtr path,
        pro_feat_elemtree_extract_opts opts,
        out IntPtr p_elem);

    // 手写覆盖: IntPtr.Zero 表示 NULL comp_path(Part 模式)。
    [DllImport(Dll, EntryPoint = "ProFeatureWithoptionsRedefine",
        CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProFeatureWithoptionsRedefine(
        IntPtr comp_path,
        ref pro_model_item feature,
        IntPtr elemtree,
        IntPtr options,
        int flags,
        ref ProErrorlist p_errors);
}
