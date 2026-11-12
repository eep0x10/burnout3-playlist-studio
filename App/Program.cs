using System.Diagnostics;
using Burnout3.PlaylistStudio.Core;

namespace Burnout3.PlaylistStudio.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--apply-update") >= 0) { ApplyUpdate(args); return; }
        var index = Array.IndexOf(args, "--data");
        var config = Array.IndexOf(args, "--config");
        bool closeAfterGame = Array.IndexOf(args, "--close-after-game") >= 0;
        int syncBeforeGame = Array.IndexOf(args, "--sync-before-game");
        try
        {
            string dataDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(AppContext.BaseDirectory, "data")));
            if (closeAfterGame && syncBeforeGame >= 0) throw new ArgumentException("Use apenas um comando do launcher por vez.");
            using var programUse = closeAfterGame ? null : PortableUpdateService.AcquireProgramUseLease(AppContext.BaseDirectory);
            if (syncBeforeGame >= 0)
            {
                ApplicationConfiguration.Initialize();
                if (syncBeforeGame + 1 >= args.Length || args[syncBeforeGame + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Informe o caminho do Takedawn.exe após --sync-before-game.");
                string game = Path.GetFullPath(args[syncBeforeGame + 1]);
                if (!File.Exists(game) || !Path.GetFileName(game).Equals("Takedawn.exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecione o Takedawn.exe existente do jogo para atualizar antes de iniciar.");
                string configPath = Path.GetFullPath(config >= 0 && config + 1 < args.Length ? args[config + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Takedawn", "settings.toml"));
                Environment.ExitCode = PrelaunchSync.Run(PrelaunchSync.CreateService(dataDirectory), configPath, game);
                if (Environment.ExitCode == 0) PortableUpdateService.MarkGameLaunchIntent(AppContext.BaseDirectory);
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
    private static void ApplyUpdate(string[] args)
    {
        Environment.ExitCode = 1;
        try
        {
            if (args.Length != 3 || args[0] != "--apply-update" || args[2].Length != 64 || !args[2].All(Uri.IsHexDigit)) throw new ArgumentException("Comando de aplicação inválido. Use o atualizador do gerenciador.");
            var result = new PortableUpdateService().ApplyAsync(Path.GetFullPath(args[1]), expectedManifestSha256: args[2]).GetAwaiter().GetResult();
            if (!result.Success) ReportUpdateFailure(result.Message + (string.IsNullOrWhiteSpace(result.BackupDirectory) ? "" : "\n\nBackup do programa: " + result.BackupDirectory + "\nO backup está em uma pasta temporária; preserve-o antes de limpar arquivos temporários."));
            if ((result.Success || result.RolledBack) && result.RestartAfterApply)
            {
                string executable = Path.GetFullPath(result.ProgramExePath);
                if (!File.Exists(executable) || !Path.GetFileName(executable).Equals("BurnoutPlaylistStudio.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Executável de reinício inválido.");
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
                foreach (string value in result.RestartArguments) start.ArgumentList.Add(value);
                if (GameIsRunning() && !result.RestartArguments.Contains("--minimized")) start.ArgumentList.Add("--minimized");
                using var process = Process.Start(start) ?? throw new IOException("O gerenciador atualizado não pôde ser reaberto.");
            }
            Environment.ExitCode = result.Success ? 0 : 1;
        }
        catch (Exception ex) { ReportUpdateFailure(ex.Message); }
    }
    private static void ReportUpdateFailure(string message)
    {
        string report = Path.Combine(Path.GetTempPath(), "BurnoutPlaylistStudio-update-error-" + Guid.NewGuid().ToString("N") + ".txt");
        try { File.WriteAllText(report, DateTime.UtcNow.ToString("O") + "\n" + message); }
        catch (IOException) { report = "Não foi possível gravar o relatório temporário."; }
        catch (UnauthorizedAccessException) { report = "Não foi possível gravar o relatório temporário."; }
        if (!GameIsRunning()) MessageBox.Show("A atualização portátil não foi concluída. Seus dados de usuário foram preservados.\n\n" + message + "\n\n" + report, "Atualização do Playlist Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    private static bool GameIsRunning()
    {
        try { var processes = Process.GetProcessesByName("Takedawn"); try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); } }
        catch (InvalidOperationException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return true; }
    }
}
