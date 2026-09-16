using System.Runtime.InteropServices;
using CreoToolkit.Interop;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    // ---- visit callback delegate 类型（Cdecl，匹配 Pro*Visit 回调约定）----

    /// <summary>通用 visit action：item=opaque handle，filterStatus=filter 返回值，appData 未用。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProVisitAction(IntPtr item, GNS.ProErrors filterStatus, IntPtr appData);

    /// <summary>ProDrawingViewVisit 专用 callback：(drawing, view, filterStatus, appData)。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProDrawingViewVisitAction(
        IntPtr drawing, IntPtr view, GNS.ProErrors filterStatus, IntPtr appData);

    /// <summary>元素树 visit callback: parent + elem + elempath + appdata。</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate GNS.ProErrors ProElemtreeVisitAction(
        IntPtr parentElem, IntPtr elem, IntPtr elempath, IntPtr appdata);

    // ---- GCHandle registry（per-instance，与 bridge 生命周期一致）----
    private readonly CallbackRegistry _visitRegistry = new();

    // ---- 通用 visit 累积 helper ----

    /// <summary>
    /// 通用 visit 累积：对 handle 调 visitor，收集 extractor 返非 null 的 T 到列表。
    /// callback 内 try/catch 隔离 C# 异常（防 SEH）；异常在 visit 完成后重抛。
    /// E_NOT_FOUND(-4)/NOT_EXIST(-23) 视为"空集合"，不抛。
    /// </summary>
    private List<T> VisitCollect<T>(
        Func<IntPtr, IntPtr, IntPtr, IntPtr, GNS.ProErrors> visitor,
        IntPtr handle,
        Func<IntPtr, GNS.ProErrors, T?> extractor)
        where T : struct
    {
        var results = new List<T>();
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            // 短路:已捕异常则不再调 extractor(避免 caught 被覆盖,首个异常丢失)。
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                // filter_status != NO_ERROR 表示 filter 拒绝，跳过
                if (filterStatus != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return GNS.ProErrors.PRO_TK_NO_ERROR;
                var value = extractor(item, filterStatus);
                if (value.HasValue)
                    results.Add(value.Value);
            }
            catch (Exception ex)
            {
                caught = ex; // 不能抛出 C# 异常回 native（SEH），记录后停止
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = visitor(handle, ptr, IntPtr.Zero, IntPtr.Zero);
            // E_NOT_FOUND / NOT_EXIST = 空集合，正常
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                // "VisitCollect" 是访问器动作标签，非 Pro Toolkit 函数，无法 nameof；NOT_FOUND 已在上方 guard。
                Check.Eval("VisitCollect", err);
            }
        }
        finally
        {
            _visitRegistry.Unregister(cb);
        }

        if (caught != null) throw caught;
        return results;
    }

    /// <summary>
    /// 通用 visit 取首个：extractor 返非 null 后，callback 返 PRO_TK_E_FOUND 停止遍历。
    /// PRO_TK_E_FOUND(-5) 本身在 visitor 返回时视为正常（不抛）。
    /// </summary>
    private T? VisitFirst<T>(
        Func<IntPtr, IntPtr, IntPtr, IntPtr, GNS.ProErrors> visitor,
        IntPtr handle,
        Func<IntPtr, GNS.ProErrors, T?> extractor)
        where T : struct
    {
        T? found = null;
        Exception? caught = null;

        ProVisitAction cb = (item, filterStatus, _) =>
        {
            // 短路:已捕异常则不再调 extractor。
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                if (filterStatus != GNS.ProErrors.PRO_TK_NO_ERROR)
                    return GNS.ProErrors.PRO_TK_NO_ERROR;
                var value = extractor(item, filterStatus);
                if (value.HasValue)
                {
                    found = value;
                    return GNS.ProErrors.PRO_TK_E_FOUND; // 提前停止
                }
            }
            catch (Exception ex)
            {
                caught = ex;
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
            return GNS.ProErrors.PRO_TK_NO_ERROR;
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = visitor(handle, ptr, IntPtr.Zero, IntPtr.Zero);
            // E_FOUND(-5) = 提前停止（正常），E_NOT_FOUND(-4)/NOT_EXIST(-23) = 空集合（正常）
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_FOUND
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                // "VisitFirst" 是访问器动作标签，非 Pro Toolkit 函数，无法 nameof；NOT_FOUND 已在上方 guard。
                Check.Eval("VisitFirst", err);
            }
        }
        finally
        {
            _visitRegistry.Unregister(cb);
        }

        if (caught != null) throw caught;
        return found;
    }

    /// <summary>遍历元素树并收集节点，callback 内隔离托管异常。</summary>
    private List<T> VisitElemtree<T>(
        IntPtr tree,
        Func<IntPtr, IntPtr, T?> extractor)
        where T : struct
        => VisitElemtreeCore(tree, extractor, G.ProElemtreeElementVisit);

    private List<T> VisitElemtreeCore<T>(
        IntPtr tree,
        Func<IntPtr, IntPtr, T?> extractor,
        Func<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, GNS.ProErrors> visitor)
        where T : struct
    {
        var results = new List<T>();
        Exception? caught = null;

        ProElemtreeVisitAction cb = (_, elem, elempath, _) =>
        {
            if (caught != null) return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            try
            {
                var value = extractor(elem, elempath);
                if (value.HasValue)
                    results.Add(value.Value);
                return GNS.ProErrors.PRO_TK_NO_ERROR;
            }
            catch (Exception ex)
            {
                caught = ex;
                return GNS.ProErrors.PRO_TK_GENERAL_ERROR;
            }
        };

        var ptr = _visitRegistry.Register(cb);
        try
        {
            var err = visitor(tree, IntPtr.Zero, IntPtr.Zero, ptr, IntPtr.Zero);
            if (err != GNS.ProErrors.PRO_TK_NO_ERROR
                && err != GNS.ProErrors.PRO_TK_E_NOT_FOUND
                && err != GNS.ProErrors.PRO_TK_NOT_EXIST)
            {
                if (caught != null) throw caught;
                // "VisitElemtree" 是访问器动作标签，非 Pro Toolkit 函数，无法 nameof；NOT_FOUND 已在上方 guard。
                Check.Eval("VisitElemtree", err);
            }
        }
        finally
        {
            _visitRegistry.Unregister(cb);
        }

        if (caught != null) throw caught;
        return results;
    }
}
