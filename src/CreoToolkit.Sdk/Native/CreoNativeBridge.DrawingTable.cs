using System.Runtime.InteropServices;
using CreoToolkit.Sdk.Diagnostics;
using CreoToolkit.Sdk.Errors;
using GNS = CreoToolkit.Interop.Generated;
using G = CreoToolkit.Interop.Generated.NativeMethods;

namespace CreoToolkit.Sdk.Native;

internal sealed partial class CreoNativeBridge
{
    /// <summary>在 drawing 上创建表格(官方序列):
    /// <c>ProDwgtabledataAlloc → OriginSet → SizetypeSet → ColumnsSet → RowsSet →
    /// ProDrawingTableCreate(display=1) → TextEnter × N → Display</c>。
    /// <para>内存契约:ProDwgtabledataAlloc 分配的 data 无对应 Free 函数(头文件 grep 确认);
    /// 假定 TableCreate 消费(真机裁决)。</para>
    /// <para>TextEnter 输入 ProArray(ProWstring):托管 ProArrayAlloc + ProArrayObjectAdd 构造,
    /// 调用后调用者 ProArrayFree + 逐元素 FreeHGlobal 释放(输入非 consuming 惯例)。</para></summary>
    public ItemRef DrawingTableCreate(ModelIdentity drawing, DrawingTableSpec spec)
    {
        ThrowUtil.IfNull(spec);
        if (!TryResolveMdl(drawing, out var mdl))
            throw new InvalidOperationException(
                $"drawing {drawing.Name} 不在会话中(需先打开到窗口)");

        // 1. Alloc + 配置 data
        Check.Eval(nameof(G.ProDwgtabledataAlloc),
            G.ProDwgtabledataAlloc(out var data));

        var origin = new double[] { spec.Origin.X, spec.Origin.Y, 0 };
        Check.Eval(nameof(G.ProDwgtabledataOriginSet),
            G.ProDwgtabledataOriginSet(data, origin));

        Check.Eval(nameof(G.ProDwgtabledataSizetypeSet),
            G.ProDwgtabledataSizetypeSet(data,
                GNS.ProDwgtableSizetype.PRODWGTABLESIZE_CHARACTERS));

        // ColumnsSet: widths + justifications 为标量/枚举数组,marshaller 自动 pin
        int nCols = spec.ColumnWidths.Length;
        var justifications = new GNS.ProHorzJust[nCols]; // 全 LEFT(0)
        Check.Eval(nameof(G.ProDwgtabledataColumnsSet),
            G.ProDwgtabledataColumnsSet(data, nCols,
                spec.ColumnWidths, justifications));

        // RowsSet: heights 为标量数组
        int nRows = spec.RowHeights.Length;
        Check.Eval(nameof(G.ProDwgtabledataRowsSet),
            G.ProDwgtabledataRowsSet(data, nRows, spec.RowHeights));

        // 2. 创建表格(display=1 立即显示)
        var table = default(GNS.pro_model_item);
        Check.Eval(nameof(G.ProDrawingTableCreate),
            G.ProDrawingTableCreate(mdl, data, 1, ref table));
        // data 无 Free(TableCreate 可能消费;无 ProDwgtabledataFree 头文件确认)

        // 3. TextEnter: 逐单元格写入
        foreach (var cell in spec.Cells)
        {
            TextEnterCell(ref table, cell.Column, cell.Row, cell.Text);
        }

        // 4. Display 刷新(失败不吞:warn 让显示失效可诊断,探针语义)
        var dispRc = G.ProDwgtableDisplay(ref table);
        if (dispRc != GNS.ProErrors.PRO_TK_NO_ERROR)
        {
            CreoSdkLog.Warn("drawing", "table-create.display_failed",
                $"ProDwgtableDisplay rc={(int)dispRc}: 表格 {table.id} 已创建但显示刷新失败",
                new { drawing = drawing.Name, tableId = table.id, rc = (int)dispRc });
        }

        CreoSdkLog.Trace("drawing", "table-create.ok",
            new
            {
                drawing = drawing.Name,
                tableId = table.id,
                columns = nCols,
                rows = nRows,
                cells = spec.Cells.Count,
            });
        return new ItemRef(drawing, CreoModelItemType.DrawingTable, table.id);
    }

    /// <summary>TextEnter 单格:托管构造 ProArray of ProWstring,调用后释放。
    /// 单行文本;多行可扩展(把 span 由 1 元素扩为 N)。
    /// ProWstring 语义 = wchar_t*(HGlobalUni),lines_arr 的元素只是指针拷贝,归 caller 释放。</summary>
    private static void TextEnterCell(
        ref GNS.pro_model_item table, int column, int row, string text)
    {
        // 文本行 → HGlobalUni(wchar_t* 含 null 终止),归 caller 释放
        var wstr = Marshal.StringToHGlobalUni(text);
        try
        {
            // 元素=ProWstring 的 ProArray,单元素喂入 helper(Alloc 失败由 helper 内 Check.Eval 抛)
            Span<IntPtr> ptrs = stackalloc IntPtr[] { wstr };
            using var scope = ProArrayMarshal.AllocPointers(ptrs);

            Check.Eval(nameof(G.ProDwgtableTextEnter),
                G.ProDwgtableTextEnter(ref table, column, row, scope.Handle));
        }
        finally
        {
            // 释放 wchar_t* 字符串(ProArray 容器由 scope 自动释放)
            Marshal.FreeHGlobal(wstr);
        }
    }

    /// <summary>读取表格单元格文本(<c>ProDwgtableCelltextGet</c>, mode=1 显示态)。
    /// 出参 <c>ProWstringproarrayFree</c> 释放。</summary>
    public string[] DrawingTableCellRead(
        ModelIdentity drawing, int tableId, int column, int row)
    {
        if (!TryResolveMdl(drawing, out var mdl))
            throw new InvalidOperationException(
                $"drawing {drawing.Name} 不在会话中");

        // 重建 table pro_model_item
        var table = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_DRAW_TABLE,
            id = tableId,
            owner = mdl,
        };

        var rc = G.ProDwgtableCelltextGet(ref table, column, row,
            GNS.ProParamMode.PRODWGTABLE_NORMAL, out var linesPtr);

        if (rc == GNS.ProErrors.PRO_TK_GENERAL_ERROR)
        {
            // 头文件: GENERAL_ERROR 含 "does not have any entered text"
            CreoSdkLog.Trace("drawing", "table-cellread.empty",
                new { drawing = drawing.Name, tableId, column, row });
            return Array.Empty<string>();
        }
        Check.Eval(nameof(G.ProDwgtableCelltextGet), rc);
        // ProWstringproarrayFree 自递归释放元素 wstring,按值签名(有别于 ref 版 ProArrayFree)
        using var scope = ProArrayScope.OwnedCustom(linesPtr, WstringProarrayFreeAction);
        if (scope.Handle == IntPtr.Zero)
            return Array.Empty<string>();

        // SizeGet 容错(失败/空 → 空列表)
        var pointers = ProArrayMarshal.ReadPointers(scope.Handle);
        if (pointers.Length == 0)
            return Array.Empty<string>();

        var result = new string[pointers.Length];
        for (int i = 0; i < pointers.Length; i++)
        {
            var wstrPtr = pointers[i];
            if (wstrPtr == IntPtr.Zero) { result[i] = string.Empty; continue; }
            // D7-EXEMPT: 元素属 ProWstringproarray 容器,finally 整数组 ProWstringproarrayFree(自递归元素),禁逐元素 ProWstringFree 防双重释放
            result[i] = Marshal.PtrToStringUni(wstrPtr) ?? string.Empty;
        }

        CreoSdkLog.Trace("drawing", "table-cellread.ok",
            new { drawing = drawing.Name, tableId, column, row, lineCount = pointers.Length });
        return result;
    }

    /// <summary>ProWstringproarrayFree 委托:按值签名(有别于 ProArrayFree 的 ref)。</summary>
    private static readonly Action<IntPtr> WstringProarrayFreeAction = ptr =>
    {
        _ = G.ProWstringproarrayFree(ptr);
    };

    /// <summary>删除表格(<c>ProDwgtableDelete</c>; 头文件 display 参数 "Ignore this argument")。</summary>
    public void DrawingTableDelete(ModelIdentity drawing, int tableId)
    {
        if (!TryResolveMdl(drawing, out var mdl))
            throw new InvalidOperationException(
                $"drawing {drawing.Name} 不在会话中");

        var table = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_DRAW_TABLE,
            id = tableId,
            owner = mdl,
        };

        Check.Eval(nameof(G.ProDwgtableDelete),
            G.ProDwgtableDelete(ref table, 1));

        CreoSdkLog.Trace("drawing", "table-delete.ok",
            new { drawing = drawing.Name, tableId });
    }

    /// <summary>表格行数(<c>ProDwgtableRowsCount</c>)。</summary>
    public int DrawingTableRowsCount(ModelIdentity drawing, int tableId)
    {
        if (!TryResolveMdl(drawing, out var mdl))
            throw new InvalidOperationException(
                $"drawing {drawing.Name} 不在会话中");

        var table = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_DRAW_TABLE,
            id = tableId,
            owner = mdl,
        };

        Check.Eval(nameof(G.ProDwgtableRowsCount),
            G.ProDwgtableRowsCount(ref table, out var count));
        return count;
    }

    /// <summary>表格列数(<c>ProDwgtableColumnsCount</c>)。</summary>
    public int DrawingTableColumnsCount(ModelIdentity drawing, int tableId)
    {
        if (!TryResolveMdl(drawing, out var mdl))
            throw new InvalidOperationException(
                $"drawing {drawing.Name} 不在会话中");

        var table = new GNS.pro_model_item
        {
            type = GNS.pro_obj_types.PRO_DRAW_TABLE,
            id = tableId,
            owner = mdl,
        };

        Check.Eval(nameof(G.ProDwgtableColumnsCount),
            G.ProDwgtableColumnsCount(ref table, out var count));
        return count;
    }
}
