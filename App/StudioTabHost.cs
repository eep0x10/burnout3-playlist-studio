namespace Burnout3.PlaylistStudio.App;

internal sealed class StudioTabHost : UserControl
{
    private readonly TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
    private readonly TableLayoutPanel header = new() { Dock = DockStyle.Fill, RowCount = 1 };
    private readonly Panel content = new() { Dock = DockStyle.Fill };
    private readonly List<(StudioButton Button, Control Page)> pages = [];
    private int selected = -1;
    internal event EventHandler? SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal int SelectedIndex
    {
        get => selected;
        set { if (value < 0 || value >= pages.Count || value == selected) return; selected = value; RefreshStyles(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    internal StudioTabHost()
    {
        Dock = DockStyle.Fill; header.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); Controls.Add(root); root.Controls.Add(header, 0, 0); root.Controls.Add(content, 0, 1);
    }
    internal void AddPage(string title, Control page)
    {
        int index = pages.Count; var button = new StudioButton { Text = title, Dock = DockStyle.Fill, AccessibleName = title };
        page.Dock = DockStyle.Fill; content.Controls.Add(page); header.ColumnCount = index + 1; header.ColumnStyles.Clear(); for (int i = 0; i <= index; i++) header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / (index + 1))); header.Controls.Add(button, index, 0); pages.Add((button, page)); button.Click += (_, _) => SelectedIndex = index; if (selected < 0) selected = 0; RefreshStyles();
    }
    internal void RefreshStyles()
    {
        for (int i = 0; i < pages.Count; i++) { var value = pages[i]; value.Page.Visible = i == selected; value.Button.BackColor = i == selected ? StudioTheme.Orange : StudioTheme.Input; value.Button.ForeColor = i == selected ? StudioTheme.Asphalt : StudioTheme.White; if (i == selected) value.Page.BringToFront(); }
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ScaleMetrics(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ScaleMetrics(); }
    private void ScaleMetrics() { float scale = DeviceDpi / 96f; root.RowStyles[0].Height = 40 * scale; foreach (var value in pages) value.Button.Margin = new Padding((int)(3 * scale)); RefreshStyles(); }
}

internal sealed class RacingSignature : Control
{
    internal RacingSignature() { Dock = DockStyle.Bottom; Height = 5; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(StudioTheme.Line); using var fill = new SolidBrush(StudioTheme.Orange); int end = Math.Min(Width, Width / 3); e.Graphics.FillPolygon(fill, [new Point(0, 0), new Point(end + Height, 0), new Point(end, Height), new Point(0, Height)]);
        for (int i = 1; i <= 3; i++) { int x = end + Height + i * Height * 2; e.Graphics.FillPolygon(fill, [new Point(x, 0), new Point(x + Height, 0), new Point(x, Height), new Point(x - Height, Height)]); }
    }
}
