using System.Runtime.InteropServices;

namespace Burnout3.PlaylistStudio.App;

internal static class StudioTheme
{
    internal static readonly Color Asphalt = Color.FromArgb(17, 18, 20), Panel = Color.FromArgb(27, 28, 31), Input = Color.FromArgb(37, 38, 42), White = Color.FromArgb(246, 242, 231), Muted = Color.FromArgb(160, 160, 158), Orange = Color.FromArgb(245, 96, 34), Red = Color.FromArgb(209, 46, 42), Line = Color.FromArgb(57, 58, 61);
    internal static void Apply(Control control)
    {
        control.BackColor = control is TextBox or ComboBox or ListBox or NumericUpDown ? Input : Panel; control.ForeColor = control.Tag as string == "accent" ? Orange : control is Label && control.Font.Size <= 10 ? Muted : White;
        if (control is Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Line; button.Cursor = Cursors.Hand; }
        if (control is TextBox text) text.BorderStyle = BorderStyle.FixedSingle;
        if (control is DataGridView grid)
        {
            grid.BackgroundColor = Asphalt; grid.BorderStyle = BorderStyle.None; grid.EnableHeadersVisualStyles = false; grid.GridColor = Line;
            grid.DefaultCellStyle.BackColor = Asphalt; grid.DefaultCellStyle.ForeColor = White; grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(80, 42, 28); grid.DefaultCellStyle.SelectionForeColor = White; grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(23, 24, 27);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Input; grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted; grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold); grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None; grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        }
        foreach (Control child in control.Controls) Apply(child);
        if (control is StudioTabHost tabs) tabs.RefreshStyles();
    }
    internal static Label Caption(string text, float size = 10, bool bold = false) => new() { Text = text, AutoSize = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    internal static void DarkTitle(Form form) { try { int dark = 1; DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int)); } catch { } }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal sealed class StudioButton : Button
{
    protected override void OnPaint(PaintEventArgs e)
    {
        using var fill = new SolidBrush(BackColor); e.Graphics.FillRectangle(fill, ClientRectangle);
        using var pen = new Pen(Focused ? StudioTheme.Orange : StudioTheme.Line); e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -5, -2), Enabled ? ForeColor : Color.FromArgb(187, 186, 181), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4), ForeColor, BackColor);
    }
}
