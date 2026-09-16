using System.Drawing;
using System.Windows.Forms;

namespace CreoToolkit.Samples.Shell.WinForms.Theme;

// 表格: 行高 36 + 表头浅灰粗体 + accent 选中行 + 无格线 + 左侧 Danger 色带。
internal sealed class FluentDataGridView : DataGridView
{
    public FluentDataGridView()
    {
        BackgroundColor = FluentPalette.CardBackground;
        BorderStyle = BorderStyle.None;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        GridColor = FluentPalette.Divider;
        EnableHeadersVisualStyles = false;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        ReadOnly = true;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        Font = FluentPalette.Body;
        RowTemplate.Height = 36;
        DoubleBuffered = true;

        ColumnHeadersHeight = 40;
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = FluentPalette.WindowBackground,
            ForeColor = FluentPalette.TextSecondary,
            Font = FluentPalette.BodyBold,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(FluentPalette.SpaceMd, 0, FluentPalette.SpaceSm, 0),
            SelectionBackColor = FluentPalette.WindowBackground,
            SelectionForeColor = FluentPalette.TextSecondary,
        };

        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = FluentPalette.CardBackground,
            ForeColor = FluentPalette.TextPrimary,
            Font = FluentPalette.Body,
            Alignment = DataGridViewContentAlignment.MiddleLeft,
            Padding = new Padding(FluentPalette.SpaceMd, 0, FluentPalette.SpaceSm, 0),
            SelectionBackColor = FluentPalette.RowSelected,
            SelectionForeColor = FluentPalette.TextPrimary,
        };
    }
}
