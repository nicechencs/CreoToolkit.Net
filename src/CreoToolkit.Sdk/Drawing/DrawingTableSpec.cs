namespace CreoToolkit.Sdk;

/// <summary>工程图表格创建参数(纯值对象,Bridge 消费后不再引用)。
/// <para>列宽/行高单位为字符数(对应 <c>PRODWGTABLESIZE_CHARACTERS</c>);
/// 单元格索引 1-based(与 Creo API 一致)。</para></summary>
public sealed class DrawingTableSpec
{
    /// <summary>表格放置原点(图纸坐标, z 忽略)。</summary>
    public (double X, double Y) Origin { get; }

    /// <summary>各列宽度(字符单位);长度即列数,上限 50。</summary>
    public double[] ColumnWidths { get; }

    /// <summary>各行高度(字符单位);长度即行数,上限 100。</summary>
    public double[] RowHeights { get; }

    /// <summary>待写入的单元格文本;索引 1-based。</summary>
    public IReadOnlyList<DrawingTableCellText> Cells { get; }

    public DrawingTableSpec(
        (double X, double Y) origin,
        double[] columnWidths,
        double[] rowHeights,
        IReadOnlyList<DrawingTableCellText> cells)
    {
        ThrowUtil.IfNull(columnWidths);
        ThrowUtil.IfNull(rowHeights);
        ThrowUtil.IfNull(cells);

        if (columnWidths.Length == 0)
            throw new ArgumentException("至少需要一列", nameof(columnWidths));
        if (columnWidths.Length > 50)
            throw new ArgumentException($"列数 {columnWidths.Length} 超过上限 50", nameof(columnWidths));

        if (rowHeights.Length == 0)
            throw new ArgumentException("至少需要一行", nameof(rowHeights));
        if (rowHeights.Length > 100)
            throw new ArgumentException($"行数 {rowHeights.Length} 超过上限 100", nameof(rowHeights));

        foreach (var cell in cells)
        {
            if (cell.Column < 1 || cell.Column > columnWidths.Length)
                throw new ArgumentException(
                    $"单元格列索引 {cell.Column} 超出范围 [1, {columnWidths.Length}]", nameof(cells));
            if (cell.Row < 1 || cell.Row > rowHeights.Length)
                throw new ArgumentException(
                    $"单元格行索引 {cell.Row} 超出范围 [1, {rowHeights.Length}]", nameof(cells));
        }

        Origin = origin;
        ColumnWidths = columnWidths;
        RowHeights = rowHeights;
        Cells = cells;
    }
}

/// <summary>单元格文本(1-based 索引 + 文本内容)。</summary>
public readonly record struct DrawingTableCellText(int Column, int Row, string Text);
