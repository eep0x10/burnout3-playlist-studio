namespace Burnout3.PlaylistStudio.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var index = Array.IndexOf(args, "--data");
        var config = Array.IndexOf(args, "--config");
        bool closeAfterGame = Array.IndexOf(args, "--close-after-game") >= 0;
        int syncBeforeGame = Array.IndexOf(args, "--sync-before-game");
        try
        {
            string dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "data")));
            if (syncBeforeGame >= 0)
            {
                ApplicationConfiguration.Initialize();
                if (syncBeforeGame + 1 >= args.Length || args[syncBeforeGame + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Informe o caminho do Takedawn.exe após --sync-before-game.");
                string game = Path.GetFullPath(args[syncBeforeGame + 1]);
                if (!File.Exists(game) || !Path.GetFileName(game).Equals("Takedawn.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecione o Takedawn.exe existente do jogo para atualizar antes de iniciar.");
                string configPath = Path.GetFullPath(config >= 0 && config + 1 < args.Length ? args[config + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Takedawn", "settings.toml"));
                Environment.ExitCode = PrelaunchSync.Run(PrelaunchSync.CreateService(dataDirectory), configPath, game);
                return;
            }
            string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dataDirectory.ToUpperInvariant())));
            string closeEventName = @"Local\BurnoutPlaylistStudio-CloseAfterGame-" + identity;
            if (closeAfterGame)
            {
                var waiting = System.Diagnostics.Stopwatch.StartNew();
                do
                {
                    if (EventWaitHandle.TryOpenExisting(closeEventName, out var existingClose))
                    {
                        using (existingClose) existingClose.Set();
                        return;
                    }
                    Thread.Sleep(100);
                } while (waiting.Elapsed < TimeSpan.FromSeconds(5));
                return;
            }
            bool enableSongNotice = Array.IndexOf(args, "--enable-song-notice") >= 0;
            using var closeRequest = new EventWaitHandle(false, EventResetMode.AutoReset, closeEventName);
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
                RegisteredWaitHandle? closeListener = null;
                form.HandleCreated += (_, _) =>
                {
                    enableListener ??= ThreadPool.RegisterWaitForSingleObject(enableNotice, (_, _) => form.EnableSongNoticeFromLauncher(), null, Timeout.Infinite, false);
                    closeListener ??= ThreadPool.RegisterWaitForSingleObject(closeRequest, (_, _) => form.CloseAfterGameFromLauncher(), null, Timeout.Infinite, false);
                };
                try { Application.Run(form); }
                finally { enableListener?.Unregister(null); closeListener?.Unregister(null); }
            }
            finally { instance.ReleaseMutex(); }
        }
        catch (Exception ex) { if (syncBeforeGame >= 0) Environment.ExitCode = 1; if (!closeAfterGame) MessageBox.Show((syncBeforeGame >= 0 ? "Não foi possível atualizar antes de iniciar o jogo.\n\n" : "Não foi possível abrir a biblioteca portátil. Extraia o aplicativo em uma pasta em que você possa escrever.\n\n") + ex.Message, "Burnout 3 Playlist Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
