using System.Diagnostics;
using Burnout3.PlaylistStudio.Core;

namespace Burnout3.PlaylistStudio.App;

public interface IPortableUpdateBackend
{
    Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken, bool ignoreCooldown);
    Task<StagedUpdate> StageAsync(UpdateRelease release, int parentId, DateTime parentStartUtc, IProgress<UpdateProgress> progress, CancellationToken cancellationToken, IReadOnlyList<string> restartArguments, bool restartAfterApply);
}

public sealed class UpdateCoordinator : IAsyncDisposable
{
    private readonly IPortableUpdateBackend backend;
    private readonly Func<bool, bool> canApply;
    private readonly Action<StagedUpdate, bool> handoff;
    private readonly IReadOnlyList<string> restartArguments;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? checking;
    private Task currentJob = Task.CompletedTask;
    private StagedUpdate? staged;
    private bool manualRequested, handedOff, stopped, disposed, companionRecheck;
    public Version CurrentVersion { get; }
    public bool AutomaticEnabled { get; private set; }
    public bool CompanionSession { get; private set; }
    public bool IsWorking { get; private set; }
    public bool HasPendingUpdate => staged is not null && !handedOff;
    public string Status { get; private set; } = "Aguardando verificação de atualizações.";
    public event EventHandler? StateChanged;

    public UpdateCoordinator(Version currentVersion, bool automaticEnabled, bool companionSession, IReadOnlyList<string> restartArguments, Func<bool, bool> canApply, Action<StagedUpdate, bool> handoff, IPortableUpdateBackend? backend = null)
    {
        CurrentVersion = currentVersion; AutomaticEnabled = automaticEnabled; CompanionSession = companionSession;
        this.restartArguments = restartArguments.ToArray(); this.canApply = canApply; this.handoff = handoff;
        this.backend = backend ?? new CoreBackend(new PortableUpdateService(AppContext.BaseDirectory));
        if (!automaticEnabled) Status = "Atualizações automáticas desativadas.";
    }
    public void SetAutomatic(bool enabled)
    {
        AutomaticEnabled = enabled;
        if (!enabled && !manualRequested) checking?.Cancel();
        SetStatus(enabled ? "Atualizações automáticas ativadas." : "Atualizações automáticas desativadas. A verificação manual continua disponível.");
    }
    public void Cancel() => checking?.Cancel();
    public void EnterCompanionSession()
    {
        if (CompanionSession || handedOff || stopped) return;
        CompanionSession = true; staged = null;
        if (IsWorking) { companionRecheck = true; checking?.Cancel(); }
        else _ = CheckAsync();
    }
    public Task CheckAsync(bool manual = false)
    {
        if (stopped || disposed || handedOff || IsWorking || (!manual && !AutomaticEnabled)) return currentJob;
        manualRequested |= manual; IsWorking = true;
        currentJob = CheckCoreAsync(manual);
        return currentJob;
    }
    private async Task CheckCoreAsync(bool manual)
    {
        checking = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            SetStatus("Consultando a versão estável no GitHub…");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(checking.Token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
            var result = await Task.Run(() => backend.CheckAsync(CurrentVersion, timeout.Token, manual), timeout.Token);
            checking.Token.ThrowIfCancellationRequested();
            if (!result.Available || result.Release is null) { staged = null; SetStatus(result.Message); return; }
            SetStatus($"Atualização {result.Release.Version} disponível. Preparando pacote verificado…");
            if (staged?.Version != result.Release.Version)
            {
                var reporter = new Progress<UpdateProgress>(value => { if (IsWorking && !stopped && !disposed && !handedOff) SetStatus(value.Message + (value.Percent is double percent ? $" ({percent:0}%)" : "")); });
                using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(checking.Token); downloadTimeout.CancelAfter(TimeSpan.FromMinutes(10));
                using var parent = Process.GetCurrentProcess(); int parentId = parent.Id; DateTime started = parent.StartTime.ToUniversalTime();
                staged = await Task.Run(() => backend.StageAsync(result.Release, parentId, started, reporter, downloadTimeout.Token, restartArguments, !CompanionSession), downloadTimeout.Token);
                checking.Token.ThrowIfCancellationRequested();
            }
            SetStatus(CompanionSession ? "Atualização verificada. Será aplicada ao fechar o gerenciador, se o jogo estiver fechado, sem reabri-lo." : "Atualização verificada. A aplicação aguarda o gerenciador livre, os diálogos fechados e o jogo fechado.");
        }
        catch (OperationCanceledException) { staged = null; if (!stopped) SetStatus("Verificação interrompida ou tempo limite atingido. O aplicativo continua disponível."); }
        catch (Exception ex) { if (!stopped) SetStatus("Atualização indisponível: " + ex.Message); }
        finally { checking.Dispose(); checking = null; IsWorking = false; StateChanged?.Invoke(this, EventArgs.Empty); }
        if (!stopped && companionRecheck) { companionRecheck = false; _ = CheckAsync(); }
        else if (!stopped) TryApply();
    }
    public bool TryApply(bool closing = false)
    {
        if (disposed || (stopped && !closing) || handedOff || IsWorking || staged is null || (!AutomaticEnabled && !manualRequested)) return false;
        // A companion may be milliseconds away from the launcher's game start.
        if (CompanionSession != closing) return false;
        try { if (!canApply(closing)) return false; handoff(staged, closing); handedOff = true; SetStatus("Atualização entregue ao instalador portátil. Aguardando o encerramento seguro do gerenciador…"); return true; }
        catch (Exception ex) { SetStatus("Não foi possível iniciar a atualização: " + ex.Message); manualRequested = false; staged = null; return false; }
    }
    public async Task StopAsync()
    {
        if (stopped) return; stopped = true; lifetime.Cancel();
        await currentJob;
    }
    private void SetStatus(string value) { Status = value; StateChanged?.Invoke(this, EventArgs.Empty); }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        await StopAsync(); disposed = true; lifetime.Dispose();
    }
    private sealed class CoreBackend(PortableUpdateService service) : IPortableUpdateBackend
    {
        public Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken, bool ignoreCooldown) => service.CheckAsync(current, cancellationToken, ignoreCooldown);
        public Task<StagedUpdate> StageAsync(UpdateRelease release, int parentId, DateTime parentStartUtc, IProgress<UpdateProgress> progress, CancellationToken cancellationToken, IReadOnlyList<string> restartArguments, bool restartAfterApply)
        {
            string? running = Environment.ProcessPath;
            if (running is null || !Path.GetFileName(running).Equals("BurnoutPlaylistStudio.exe", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(AppContext.BaseDirectory, "BurnoutPlaylistStudio.dll"))) throw new InvalidOperationException("A aplicação automática requer o executável portátil distribuído. Esta execução de desenvolvimento não será substituída.");
            return service.StageAsync(release, parentId, parentStartUtc, progress, cancellationToken, restartArguments, restartAfterApply);
        }
    }
}

internal sealed class UpdateSettingsDialog : Form
{
    internal UpdateSettingsDialog(UpdateCoordinator coordinator, Action<bool> savePreference)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Atualizações do Playlist Studio"; ClientSize = new Size(620, 320); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 30, 36, 106, 58, 48 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(StudioTheme.Caption("Versão instalada: " + coordinator.CurrentVersion, 13, true), 0, 0);
        var automatic = new CheckBox { Text = "Atualizações automáticas", Checked = coordinator.AutomaticEnabled, AutoSize = true, Dock = DockStyle.Fill }; root.Controls.Add(automatic, 0, 1);
        var status = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = coordinator.Status }; root.Controls.Add(status, 0, 2);
        root.Controls.Add(new Label { Text = "Pacotes oficiais do GitHub. Seus dados, músicas e configurações são preservados. A aplicação aguarda fechar este diálogo e o jogo; nunca fecha o jogo por você.", Dock = DockStyle.Fill, AutoSize = false }, 0, 3);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = new StudioButton { Text = "Fechar", Width = 100, Height = 34, DialogResult = DialogResult.Cancel };
        var check = new StudioButton { Text = "Verificar e instalar", Width = 190, Height = 34 }; actions.Controls.Add(close); actions.Controls.Add(check); root.Controls.Add(actions, 0, 4); Controls.Add(root); CancelButton = close;
        void Render() { if (IsDisposed) return; status.Text = coordinator.Status; check.Text = coordinator.IsWorking ? "Cancelar verificação" : "Verificar e instalar"; }
        EventHandler changed = (_, _) => Render(); coordinator.StateChanged += changed;
        automatic.CheckedChanged += (_, _) => { coordinator.SetAutomatic(automatic.Checked); savePreference(automatic.Checked); };
        check.Click += async (_, _) => { if (coordinator.IsWorking) coordinator.Cancel(); else await coordinator.CheckAsync(true); };
        FormClosed += (_, _) => coordinator.StateChanged -= changed; Shown += (_, _) => { StudioTheme.DarkTitle(this); Render(); }; StudioTheme.Apply(this); ResumeLayout(true);
    }
}
