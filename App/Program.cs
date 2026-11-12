namespace Burnout3.PlaylistStudio.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        var config = Array.IndexOf(args, "--config");
        try
        {
            string dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "data")));
            string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())));
            bool enableSongNotice = Array.IndexOf(args, "--enable-song-notice") >= 0;
            using var enableNotice = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BurnoutPlaylistStudio-EnableNotice-" + identity);
            using var instance = new Mutex(false, @"Local\BurnoutPlaylistStudio-" + identity);
            bool acquired;
            try { acquired = instance.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) { if (enableSongNotice) enableNotice.Set(); return; }
            try
            {
                ApplicationConfiguration.Initialize();
                using var form = new StudioForm(dataDirectory, config >= 0 && config + 1 < args.Length ? args[config + 1] : null, Array.IndexOf(args, "--preview-toast") >= 0, Array.IndexOf(args, "--minimized") >= 0, enableSongNotice);
                RegisteredWaitHandle? enableListener = null;
                form.HandleCreated += (_, _) => enableListener ??= ThreadPool.RegisterWaitForSingleObject(enableNotice, (_, _) => form.EnableSongNoticeFromLauncher(), null, Timeout.Infinite, false);
                try { Application.Run(form); }
                finally { enableListener?.Unregister(null); }
            }
            finally { instance.ReleaseMutex(); }
        }
        catch (Exception ex) { MessageBox.Show("Não foi possível abrir a biblioteca portátil. Extraia o aplicativo em uma pasta em que você possa escrever.\n\n" + ex.Message, "Burnout 3 Playlist Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
