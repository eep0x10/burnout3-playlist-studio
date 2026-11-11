using System.Runtime.InteropServices;
using System.Text;

namespace Burnout3.PlaylistStudio.App;

internal sealed class AudioPreview : IDisposable
{
    private readonly string alias = "b3preview" + Guid.NewGuid().ToString("N");
    internal bool IsPlaying { get; private set; }
    internal bool HasTrack { get; private set; }
    internal void Open(string file)
    {
        Close();
        if (!File.Exists(file)) throw new FileNotFoundException("O arquivo de áudio não foi encontrado.", file);
        if (file.Contains('"')) throw new ArgumentException("Caminho de áudio inválido.");
        try { Run($"open \"{Path.GetFullPath(file)}\" type mpegvideo alias {alias}"); HasTrack = true; Run($"set {alias} time format milliseconds"); }
        catch { Close(); throw; }
    }
    internal void Play() { if (!HasTrack) return; Run($"play {alias}"); IsPlaying = true; }
    internal void Pause() { if (!HasTrack) return; Run($"pause {alias}"); IsPlaying = false; }
    internal void Stop() { if (!HasTrack) return; mciSendString($"stop {alias}", null, 0, IntPtr.Zero); IsPlaying = false; }
    internal void Seek(int milliseconds) { if (!HasTrack) return; bool resume = IsPlaying; Run($"seek {alias} to {Math.Max(0, milliseconds)}"); if (resume) Play(); }
    internal void Volume(int percent) { if (HasTrack) Run($"setaudio {alias} volume to {Math.Clamp(percent, 0, 100) * 10}"); }
    internal int Position => Query("position");
    internal int Length => Query("length");
    internal bool Ended => HasTrack && IsPlaying && QueryText("mode") == "stopped";
    private int Query(string what) => int.TryParse(QueryText(what), out int value) ? value : 0;
    private string QueryText(string what) { if (!HasTrack) return ""; var result = new StringBuilder(256); return mciSendString($"status {alias} {what}", result, result.Capacity, IntPtr.Zero) == 0 ? result.ToString() : ""; }
    private void Run(string command) { int error = mciSendString(command, null, 0, IntPtr.Zero); if (error == 0) return; var message = new StringBuilder(256); mciGetErrorString(error, message, message.Capacity); throw new InvalidOperationException("Reprodução de áudio: " + message); }
    internal void Close() { mciSendString($"close {alias}", null, 0, IntPtr.Zero); IsPlaying = false; HasTrack = false; }
    public void Dispose() => Close();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern int mciSendString(string command, StringBuilder? result, int length, IntPtr callback);
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool mciGetErrorString(int error, StringBuilder result, int length);
}
