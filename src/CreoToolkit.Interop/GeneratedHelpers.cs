// 构建期生成绑定无法覆盖的手添 P/Invoke 兜底。新增 overload 请注明 generator 满足什么条件后可删除。

using System;
using System.Runtime.InteropServices;
using CreoToolkit.Interop.Generated;

namespace CreoToolkit.Interop;

internal static class GeneratedHelpers
{
    private const string Dll = "CreoToolkit.NativeHost.dll";

    // ProDrawingDtlnoteVisit 的 symbol 参数在 Generated 产为 ref pro_model_item，无法传 NULL
    // （枚举 drawing 自身注释时需传 NULL）。generator 支持可空指针形态后可删此 overload。
    [DllImport(Dll, EntryPoint = "ProDrawingDtlnoteVisit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProDrawingDtlnoteVisit(
        IntPtr drawing, IntPtr symbol, int sheet, IntPtr visit_action, IntPtr filter_action, IntPtr appdata);

    // ProDtlentityCreate 的 symbol 参数在 Generated 产为 ref pro_model_item，无法传 NULL。
    // ProDtlentity.h: "If you are adding a detail item to the owner, set this argument to NULL"
    // ——向 drawing 本体加 draft entity 必须传 NULL。generator 支持可空指针形态后可删此 overload。
    [DllImport(Dll, EntryPoint = "ProDtlentityCreate", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProDtlentityCreate(
        IntPtr owner, IntPtr symbol, IntPtr entdata, ref pro_model_item entity);

    // ProAnnotationShow 的 comp_path 参数在 Generated 产为 ref pro_comp_path,无法传 NULL。
    // ProAnnotation.h: comp_path "Pass NULL when not required"——顶层模型场景必须传 NULL
    // (零结构体 ≠ NULL 指针)。generator 支持可空指针形态后可删此 overload。
    [DllImport(Dll, EntryPoint = "ProAnnotationShow", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProAnnotationShow(
        ref pro_model_item annotation, IntPtr comp_path, IntPtr view);

    // ProSelectionAlloc 的 p_cmp_path 参数在 Generated 产为 ref pro_comp_path,无法传 NULL。
    // ProSelection.h:196-214 组合表:(NULL, !NULL)/(NULL, NULL) 是合法组合,path=NULL 仅限
    // part 场景(assembly 必须给 path;零结构体 ≠ NULL 指针)。generator 支持可空指针形态后可删。
    [DllImport(Dll, EntryPoint = "ProSelectionAlloc", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProSelectionAlloc(
        IntPtr p_cmp_path, ref pro_model_item p_mdl_itm, out IntPtr p_selection);

    // ProSplinedataInit/ProBsplinedataInit 未被 generator 产出(ProPoint3d*/double* 输入数组形态
    // 不在启发式覆盖内)。exports.def 已导出。ProCurvedata.h: 入参是 ProArray
    // (par_arr/pnt_arr/tan_arr; params/weights/c_pnts),调用方经 ProArrayAlloc 构造后传首址;
    // weights 对非有理 B-spline 可传 NULL。generator 覆盖 in-ProArray 形态后可删。
    [DllImport(Dll, EntryPoint = "ProSplinedataInit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProSplinedataInit(
        IntPtr par_arr, IntPtr pnt_arr, IntPtr tan_arr, int num_points, IntPtr p_curve);

    [DllImport(Dll, EntryPoint = "ProBsplinedataInit", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern ProErrors ProBsplinedataInit(
        int degree, IntPtr @params, IntPtr weights, IntPtr c_pnts,
        int num_knots, int num_c_points, IntPtr p_curve);
}
