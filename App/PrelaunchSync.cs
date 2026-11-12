using System.Diagnostics;
using Burnout3.PlaylistStudio.Core;

namespace Burnout3.PlaylistStudio.App;

public interface IPrelaunchSyncService
{
    bool IsGameRunning();
    Guid? ActivePlaylistId { get; }
    Task<SyncResult> SyncAsync(Guid playlistId, string? configPath, string gameExecutable, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
}

public static class PrelaunchSync
{
    public static async Task<SyncResult> PrepareAsync(IPrelaunchSyncService service, string? configPath, string gameExecutable, IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (service.IsGameRunning()) throw new InvalidOperationException("Feche o Takedawn antes de atualizar a soundtrack pelo atalho. Nenhum jogo foi iniciado.");
        Guid playlistId = service.ActivePlaylistId ?? throw new InvalidOperationException("Nenhuma playlist ativa. Abra o Playlist Studio, escolha uma playlist e clique em Usar no jogo antes de usar este atalho.");
        var result = await service.SyncAsync(playlistId, configPath, gameExecutable, progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (service.IsGameRunning()) throw new InvalidOperationException("O Takedawn foi aberto durante a atualização. A abertura pelo atalho foi cancelada.");
        if (result.Failed != 0 || result.PendingActivation || !result.AppliedToGame)
            throw new InvalidOperationException("A soundtrack não ficou pronta para iniciar. Confira a playlist no gerenciador e tente novamente.\n\n" + result.Message);
        return result;
    }

    public static int Run(IPrelaunchSyncService service, string? configPath, string gameExecutable)
    {
        using var form = new ProgressWindow(service, configPath, gameExecutable);
        Application.Run(form);
        return form.ExitCode;
    }

    internal static IPrelaunchSyncService CreateService(string dataDirectory) => new CoreService(dataDirectory);

    private sealed class CoreService(string dataDirectory) : IPrelaunchSyncService
    {
        private PlaylistService? library;
        private PlaylistService Service => library ??= new PlaylistService(dataDirectory);
        public Guid? ActivePlaylistId => Service.GetLibrary().ActivePlaylistId;
        public bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName("Takedawn");
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        public Task<SyncResult> SyncAsync(Guid playlistId, string? configPath, string gameExecutable, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
            => Service.SyncPlaylistAsync(playlistId, refreshRemote: true, configPath: configPath, executablePath: gameExecutable, progress: progress, cancellationToken: cancellationToken);
    }

    private sealed class ProgressWindow : Form
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly TextBox status = new() { Dock = DockStyle.Fill, Text = "Consultando a playlist ativa…", Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };
        private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Minimum = 0, Maximum = 100 };
        private readonly Button cancel = new StudioButton { Text = "Cancelar", Width = 120, Height = 34 };
        private bool working = true, finished;
        public int ExitCode { get; private set; } = 2;

        internal ProgressWindow(IPrelaunchSyncService service, string? configPath, string gameExecutable)
        {
            SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Atualizando soundtrack — Playlist Studio"; ClientSize = new Size(640, 260); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 38, 92, 15, 26, 48 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var heading = StudioTheme.Caption("SUA TRILHA ANTES DA CORRIDA", 17, true); heading.Font = new Font("Bahnschrift", 17, FontStyle.Bold); root.Controls.Add(heading, 0, 0); root.Controls.Add(status, 0, 1); root.Controls.Add(progress, 0, 2);
            root.Controls.Add(StudioTheme.Caption("A abertura do jogo aguarda esta atualização.", 9), 0, 3);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false }; actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4); Controls.Add(root);
            cancel.Click += (_, _) => { if (!working) Close(); else RequestCancellation(); };
            CancelButton = cancel;
            FormClosing += (_, e) => { if (working) { e.Cancel = true; RequestCancellation(); } };
            Shown += async (_, _) =>
            {
                StudioTheme.DarkTitle(this);
                var reporter = new Progress<OperationProgress>(value =>
                {
                    if (finished || IsDisposed || cancellation.IsCancellationRequested) return;
                    status.Text = value.Message;
                    if (value.Percent is double percent) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Clamp((int)percent, 0, 100); }
                    else progress.Style = ProgressBarStyle.Marquee;
                });
                try
                {
                    var result = await Task.Run(() => PrepareAsync(service, configPath, gameExecutable, reporter, cancellation.Token), cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!result.Complete)
                    {
                        progress.Style = ProgressBarStyle.Continuous; progress.Value = 100; status.Text = "A origem retornou uma lista parcial. As faixas anteriores foram preservadas.";
                        var choice = MessageBox.Show(this, "Não foi possível confirmar a playlist remota completa. As faixas anteriores foram preservadas; remoções remotas não foram aplicadas.\n\nContinuar com a lista preservada e permitir a abertura do jogo?\n\nO token Spotify desta sessão do gerenciador não está disponível nesta atualização separada.", "Playlist parcial — confirmar abertura", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                        if (choice != DialogResult.Yes) { Finish(2); return; }
                    }
                    Finish(0);
                }
                catch (OperationCanceledException) { Finish(2); }
                catch (Exception ex)
                {
                    finished = true; working = false; ExitCode = 1; progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; status.Text = ex.Message; cancel.Text = "Fechar"; cancel.Enabled = true;
                }
            };
            StudioTheme.Apply(this); ResumeLayout(true);
        }
        private void RequestCancellation() { if (cancellation.IsCancellationRequested) return; cancellation.Cancel(); status.Text = "Cancelando e aguardando a liberação dos arquivos…"; cancel.Enabled = false; }
        private void Finish(int code) { finished = true; working = false; ExitCode = code; Close(); }
        protected override void Dispose(bool disposing) { if (disposing) cancellation.Dispose(); base.Dispose(disposing); }
    }
}
