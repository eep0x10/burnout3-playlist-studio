using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Burnout3.PlaylistStudio.App;

/// <summary>External, passive HUD. It never activates a window or hooks the game's renderer.</summary>
internal sealed class NowPlayingOverlay : Form
{
    private const int WmNcHitTest = 0x84, WmMouseActivate = 0x21;
    private readonly IntPtr managerWindow;
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 30 };
    private readonly WinEventDelegate foregroundChanged;
    private IntPtr foregroundHook;
    private int gameProcessId;
    private bool demonstration;
    private bool notificationVisible;
    private long started;
    private double startOpacity;
    private double currentOpacity;
    private float scale = 1;
    private string songTitle = "", artist = "";
    private string targetUnavailable = "janela não disponível";
    private Font? titleFont, artistFont, brandFont;
    private const double LifetimeMilliseconds = 6000, TargetOpacity = .94;

    internal NowPlayingOverlay(IntPtr manager)
    {
        managerWindow = manager;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; ControlBox = false;
        AutoScaleMode = AutoScaleMode.None; StartPosition = FormStartPosition.Manual; TabStop = false;
        BackColor = Color.Fuchsia; TransparencyKey = Color.Fuchsia;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AccessibleRole = AccessibleRole.Alert; Text = "B3 CUSTOM TRAX";
        foregroundChanged = (_, _, _, _, _, _, _) =>
        {
            if (!notificationVisible || IsDisposed) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed && !TryGetTarget(out _, out _)) CancelNotification(targetUnavailable); })); }
            catch (InvalidOperationException) { }
        };
        foregroundHook = SetWinEventHook(3, 3, IntPtr.Zero, foregroundChanged, 0, 0, 0);
        clock.Tick += (_, _) => TickNotification();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.Style &= ~0x10000000; value.ExStyle |= 0x08000000 | 0x00000020 | 0x00000080 | 0x00080000; return value; }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmNcHitTest) { message.Result = new IntPtr(-1); return; }
        if (message.Msg == WmMouseActivate) { message.Result = new IntPtr(3); return; }
        base.WndProc(ref message);
    }

    internal bool ShowTrack(string title, string performer, int processId) => BeginNotification(title, performer, processId, false);
    internal bool ShowDemonstration() => BeginNotification("Faixa de demonstração", "Prévia do gerenciador · dados de demonstração", 0, true);
    internal void CancelNotification() => CancelNotification("aviso encerrado");
    private void CancelNotification(string reason) { clock.Stop(); if (IsHandleCreated) ShowWindow(Handle, 0); notificationVisible = false; SetOpacity(0); Text = "B3 CUSTOM TRAX — " + reason; }

    private bool BeginNotification(string title, string performer, int processId, bool demo)
    {
        if (IsDisposed) return false;
        demonstration = demo; gameProcessId = processId;
        if (!TryGetTarget(out var window, out var area)) { CancelNotification(targetUnavailable); return false; }
        songTitle = string.IsNullOrWhiteSpace(title) ? "Faixa sem título" : title;
        artist = string.IsNullOrWhiteSpace(performer) ? "Artista não informado" : performer;
        Text = demo ? "B3 CUSTOM TRAX — DEMONSTRAÇÃO" : "B3 CUSTOM TRAX — música detectada";
        AccessibleName = (demo ? "Demonstração. " : "Música detectada. ") + songTitle + ". " + artist;
        startOpacity = notificationVisible ? currentOpacity : 0; started = Stopwatch.GetTimestamp();
        if (!Position(window, area)) { CancelNotification("janela pequena para o aviso"); return false; }
        using (new PerMonitorContext())
        {
            _ = Handle;
            SetOpacity(AnimationEnabled() ? startOpacity : TargetOpacity);
            ShowWindow(Handle, 4); // SW_SHOWNOACTIVATE; avoid Form.Show activation bookkeeping.
            if (!SetWindowPos(Handle, new IntPtr(-1), Left, Top, Width, Height, 0x0010 | 0x0040)) { CancelNotification("não foi possível exibir o aviso"); return false; }
        }
        notificationVisible = true;
        Invalidate(); clock.Interval = 30; clock.Start(); return true;
    }

    private void TickNotification()
    {
        if (!TryGetTarget(out var window, out var area)) { CancelNotification(targetUnavailable); return; }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= LifetimeMilliseconds) { CancelNotification("aviso encerrado após seis segundos"); return; }
        if (!Position(window, area)) { CancelNotification("janela pequena para o aviso"); return; }
        bool animate = AnimationEnabled();
        double alpha = !animate ? TargetOpacity : elapsed < 180 ? startOpacity + (TargetOpacity - startOpacity) * (1 - Math.Pow(1 - elapsed / 180, 3)) : elapsed > LifetimeMilliseconds - 260 ? TargetOpacity * (LifetimeMilliseconds - elapsed) / 260 : TargetOpacity;
        SetOpacity(Math.Clamp(alpha, 0, TargetOpacity));
        clock.Interval = animate && (elapsed < 180 || elapsed > LifetimeMilliseconds - 260) ? 30 : 80;
    }

    private bool TryGetTarget(out IntPtr window, out Rectangle area)
    {
        window = IntPtr.Zero; area = Rectangle.Empty;
        using var context = new PerMonitorContext();
        IntPtr foreground = GetForegroundWindow(); if (foreground == IntPtr.Zero) { targetUnavailable = "nenhuma janela em primeiro plano"; return false; }
        window = GetAncestor(foreground, 2);
        if (window == IntPtr.Zero || !IsWindowVisible(window) || IsIconic(window)) { targetUnavailable = "janela oculta ou minimizada"; return false; }
        if (demonstration) { if (window != managerWindow) { targetUnavailable = "gerenciador fora de foco"; return false; } }
        else
        {
            GetWindowThreadProcessId(window, out uint id); if (id != gameProcessId || gameProcessId <= 0) return false;
            try { using var process = Process.GetProcessById(gameProcessId); if (!process.ProcessName.Equals("Takedawn", StringComparison.OrdinalIgnoreCase)) return false; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        if (!GetClientRect(window, out var rect)) { targetUnavailable = "área da janela indisponível"; return false; }
        var origin = new NativePoint(); if (!ClientToScreen(window, ref origin)) { targetUnavailable = "posição da janela indisponível"; return false; }
        area = Rectangle.FromLTRB(origin.X, origin.Y, origin.X + rect.Right, origin.Y + rect.Bottom);
        area.Intersect(Screen.FromHandle(window).Bounds);
        if (area.Width < 280 || area.Height < 180) { targetUnavailable = "área visível da janela insuficiente"; return false; } return true;
    }

    private bool Position(IntPtr window, Rectangle area)
    {
        using var context = new PerMonitorContext();
        uint dpi; try { dpi = GetDpiForWindow(window); } catch (EntryPointNotFoundException) { dpi = 96; }
        float nextScale = Math.Max(1, dpi / 96f);
        if (titleFont is null || Math.Abs(scale - nextScale) > .01f)
        {
            scale = nextScale; titleFont?.Dispose(); artistFont?.Dispose(); brandFont?.Dispose();
            titleFont = new Font("Bahnschrift SemiCondensed", 22 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
            artistFont = new Font("Segoe UI", 14 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            brandFont = new Font("Segoe UI", 9 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        }
        int margin = Px(32), bottom = area.Height >= Px(400) ? Px(80) : Px(24);
        int width = Math.Min(Px(440), area.Width - margin * 2), height = Px(112);
        if (width < Px(220) || height + bottom > area.Height) return false;
        var target = new Rectangle(area.Left + margin, area.Bottom - bottom - height, width, height);
        if (Bounds != target) Bounds = target;
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(TransparencyKey);
        using var black = new SolidBrush(Color.FromArgb(18, 19, 22));
        e.Graphics.FillPolygon(black, [new Point(0, 0), new Point(Width - Px(20), 0), new Point(Width, Px(20)), new Point(Width - Px(10), Height), new Point(0, Height)]);
        using var orange = new SolidBrush(StudioTheme.Orange); e.Graphics.FillRectangle(orange, 0, 0, Px(3), Height);
        e.Graphics.FillPolygon(orange, [new Point(Px(18), Height - Px(6)), new Point(Width - Px(38), Height - Px(6)), new Point(Width - Px(42), Height - Px(3)), new Point(Px(18), Height - Px(3))]);
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, demonstration ? "CUSTOM TRAX  /  DEMONSTRAÇÃO" : "CUSTOM TRAX", brandFont, new Rectangle(Px(24), Px(10), Width - Px(48), Px(16)), StudioTheme.Orange, flags);
        TextRenderer.DrawText(e.Graphics, songTitle, titleFont, new Rectangle(Px(24), Px(31), Width - Px(50), Px(34)), StudioTheme.White, flags);
        TextRenderer.DrawText(e.Graphics, artist, artistFont, new Rectangle(Px(24), Px(68), Width - Px(50), Px(24)), Color.FromArgb(220, 217, 207), flags);
    }
    private int Px(int logical) => (int)Math.Round(logical * scale);
    private void SetOpacity(double value) { currentOpacity = value; if (IsHandleCreated) SetLayeredWindowAttributes(Handle, 0x00FF00FF, (byte)Math.Round(255 * value), 3); }
    private static bool AnimationEnabled() => !SystemParametersInfo(0x1042, 0, out bool enabled, 0) || enabled;
    protected override void Dispose(bool disposing)
    {
        if (disposing) { clock.Stop(); clock.Dispose(); titleFont?.Dispose(); artistFont?.Dispose(); brandFont?.Dispose(); }
        if (foregroundHook != IntPtr.Zero) { UnhookWinEvent(foregroundHook); foregroundHook = IntPtr.Zero; }
        base.Dispose(disposing);
    }

    private sealed class PerMonitorContext : IDisposable
    {
        private readonly IntPtr previous;
        internal PerMonitorContext() { try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { } }
        public void Dispose() { if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WinEventDelegate callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
