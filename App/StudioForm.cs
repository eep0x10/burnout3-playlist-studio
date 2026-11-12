using System.Diagnostics;
using System.Text.Json;
using Burnout3.PlaylistStudio.Core;

namespace Burnout3.PlaylistStudio.App;

public sealed class StudioForm : Form
{
    private readonly PlaylistService service;
    private readonly string? gameConfigurationPath;
    private readonly string uiSettingsPath;
    private string? takedawnExecutable;
    private TakedawnCompatibility? compatibility;
    private ActivePlaylistStatus? activeStatus;
    private readonly ToolTip tips = new();
    private readonly CheckBox songNoticeEnabled = new() { Text = "Aviso da música no jogo", AutoSize = true, Checked = true, Font = new Font("Segoe UI", 9) };
    private readonly Button testNotice = Button("Testar aviso", 115);
    private readonly Label watcherState = StudioTheme.Caption("Preparando detector…", 9);
    private readonly SemaphoreSlim watcherGate = new(1, 1);
    private NowPlayingOverlay? songOverlay;
    private NowPlayingWatcher? songWatcher;
    private CancellationTokenSource? watcherCancellation;
    private Task? watcherRun;
    private bool notificationClosing, notificationCleanupReady;
    private bool silentStartup;
    private LibrarySnapshot library = new([], [], "");
    private Guid? selectedPlaylist, selectedTrack, playingTrack, playingPlaylist;
    private readonly ListBox playlists = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None };
    private readonly DataGridView tracks = new() { Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "Filtrar título, artista ou álbum…" };
    private readonly Label playlistTitle = StudioTheme.Caption("Sua próxima trilha começa aqui", 20, true), count = StudioTheme.Caption("Biblioteca vazia", 10), inspectorTitle = StudioTheme.Caption("Selecione uma faixa", 16, true), inspectorArtist = StudioTheme.Caption("", 10), jobText = StudioTheme.Caption("Tudo pronto. Importe MP3s ou monte sua lista.", 10), nowPlaying = StudioTheme.Caption("NENHUMA FAIXA EM REPRODUÇÃO", 10, true), clock = StudioTheme.Caption("0:00 / 0:00", 9), setup = StudioTheme.Caption("MP3 → playlist → EA GAMES TRAX", 9);
    private readonly TextBox metadata = new() { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly RichTextBox lyrics = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false };
    private readonly Label empty = new() { Text = "NENHUMA FAIXA NA PISTA.\n\nCrie uma playlist e importe seus MP3s.\nVocê também pode montar a lista com texto,\nYouTube ou uma playlist pública do Spotify.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12) };
    private readonly Button import = Button("＋  Importar", 130), download = Button("↓  Baixar e carregar", 165), onlyDownload = Button("Só baixar", 105), tools = Button("Preparar ferramentas", 165), install = Button("Carregar playlist", 165), updateGame = Button("Atualizar Takedawn", 160), cancel = Button("Cancelar", 100), create = Button("＋  Nova playlist", 175), rename = Button("Renomear", 78), delete = Button("Remover", 70), edit = Button("Editar dados", 110), remove = Button("Remover faixa", 120), up = Button("↑", 42), down = Button("↓", 42), version = Button("Escolher versão", 150), one = Button("Baixar / tentar de novo", 190), folder = Button("Localizar Takedawn", 145), previous = Button("|◀", 44), play = Button("▶", 64), next = Button("▶|", 44);
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100 };
    private readonly TrackBar seek = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, AutoSize = false, Height = 28 }, volume = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = 70, TickStyle = TickStyle.None, AutoSize = false, Height = 28 };
    private readonly AudioPreview audio = new();
    private readonly Icon? studioIcon;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 400 };
    private CancellationTokenSource? operation;
    private bool busy, filling, closeAfterOperation;
    private int inspectorGeneration;
    private int operationRevision;
    private int activationRevision;
    private bool activationVerifying;
    private ImportMode lastImportMode;
    private string lastImportText = "";
    private string[] lastImportFiles = [];
    private readonly Panel tableSurface = new() { Dock = DockStyle.Fill };

    public StudioForm(string? dataDirectory, string? configurationPath = null, bool previewToast = false, bool startMinimized = false, bool enableSongNotice = false)
    {
        silentStartup = startMinimized;
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Burnout 3 Playlist Studio"; ClientSize = new Size(1240, 800); MinimumSize = new Size(1080, 680); StartPosition = FormStartPosition.CenterScreen;
        service = new PlaylistService(dataDirectory); gameConfigurationPath = Path.GetFullPath(configurationPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Takedawn", "settings.toml"));
        uiSettingsPath = Path.Combine(Path.GetFullPath(dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "data")), "ui-settings.json");
        try { if (File.Exists(uiSettingsPath)) { var preferences = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(uiSettingsPath)); takedawnExecutable = preferences?.TakedawnExecutable; songNoticeEnabled.Checked = preferences?.SongNoticeEnabled ?? true; } } catch { }
        if (enableSongNotice) { songNoticeEnabled.Checked = true; SaveUiPreferences(); }
        using (var stream = typeof(StudioForm).Assembly.GetManifestResourceStream("Studio.ico")) if (stream is not null) { using var source = new Icon(stream); studioIcon = (Icon)source.Clone(); Icon = studioIcon; }
        BuildLayout(); ConfigureGrid(); WireActions(); StudioTheme.Apply(this); BackColor = StudioTheme.Asphalt; import.BackColor = download.BackColor = StudioTheme.Orange; import.ForeColor = download.ForeColor = StudioTheme.Asphalt; play.BackColor = StudioTheme.Orange; play.ForeColor = StudioTheme.Asphalt;
        Shown += (_, _) => { RefreshLibrary(); timer.Start(); _ = UpdateSongWatcher(); if (previewToast) BeginInvoke((Action)ShowNoticeDemonstration); };
        FormClosing += (_, e) => { if (busy) { e.Cancel = true; closeAfterOperation = true; operation?.Cancel(); SetStatus("Cancelando a operação antes de fechar…"); } else if (!notificationCleanupReady) { e.Cancel = true; _ = StopNotificationsAndClose(); } };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); audio.Dispose(); operation?.Dispose(); songOverlay?.Dispose(); watcherGate.Dispose(); tips.Dispose(); studioIcon?.Dispose(); };
        if (startMinimized) WindowState = FormWindowState.Minimized;
        ResumeLayout(true);
    }
    protected override bool ShowWithoutActivation => silentStartup;
    protected override void OnShown(EventArgs e) { StudioTheme.DarkTitle(this); var area = Screen.FromControl(this).WorkingArea; float factor = DeviceDpi / 96f; Size = new Size(Math.Min((int)(1260 * factor), area.Width), Math.Min((int)(850 * factor), area.Height)); Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2); ApplyDpi(); base.OnShown(e); silentStartup = false; }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); ApplyDpi(); }
    private void ApplyDpi() { int row = (int)(38 * DeviceDpi / 96f); tracks.RowTemplate.Height = row; foreach (DataGridViewRow value in tracks.Rows) value.Height = row; playlists.ItemHeight = (int)(34 * DeviceDpi / 96f); }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20) }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100)); Controls.Add(root);
        var header = new Panel { Dock = DockStyle.Fill }; var brand = new Panel { Dock = DockStyle.Left, Width = 275 }; brand.Controls.Add(new Label { Text = "BURNOUT 3", Dock = DockStyle.Fill, Font = new Font("Bahnschrift SemiCondensed", 28, FontStyle.Bold), ForeColor = StudioTheme.White }); brand.Controls.Add(new Label { Text = "PLAYLIST STUDIO  /  CUSTOM TRAX", Tag = "accent", Dock = DockStyle.Bottom, Height = 28, Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = StudioTheme.Orange });
        var headerActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 4, 0, 0) }; headerActions.Controls.Add(install); headerActions.Controls.Add(import); headerActions.Controls.Add(tools); headerActions.Controls.Add(updateGame); header.Controls.Add(headerActions);
        var noticePreferences = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 32, WrapContents = false }; songNoticeEnabled.Margin = new Padding(4, 5, 8, 2); testNotice.Height = 24; testNotice.Font = new Font("Segoe UI", 9); testNotice.Margin = new Padding(4, 2, 8, 2); watcherState.AutoSize = false; watcherState.Width = 240; watcherState.Height = 24; watcherState.AutoEllipsis = true; watcherState.TextAlign = ContentAlignment.MiddleLeft; noticePreferences.Controls.Add(songNoticeEnabled); noticePreferences.Controls.Add(testNotice); noticePreferences.Controls.Add(watcherState); header.Controls.Add(noticePreferences); header.Controls.Add(brand); header.Controls.Add(new RacingSignature()); root.Controls.Add(header, 0, 0);
        tips.SetToolTip(songNoticeEnabled, "Requer o gerenciador aberto e o Takedawn em primeiro plano. É uma sobreposição externa; tela cheia exclusiva pode não exibi-la."); tips.SetToolTip(testNotice, "Mostra uma demonstração de 6 segundos, sem abrir nem consultar o jogo. Não ativa outra janela.");
        setup.Text = "MP3 → PLAYLIST\nEA GAMES TRAX"; setup.AutoSize = false; setup.TextAlign = ContentAlignment.MiddleLeft;
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 }; body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 290)); root.Controls.Add(body, 0, 1);
        var rail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(0, 0, 14, 0) }; rail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 30)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); rail.Controls.Add(StudioTheme.Caption("SUAS PLAYLISTS", 9, true), 0, 0); rail.Controls.Add(create, 0, 1); rail.Controls.Add(playlists, 0, 2); var railActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; railActions.Controls.Add(rename); railActions.Controls.Add(delete); rail.Controls.Add(railActions, 0, 3); rail.Controls.Add(setup, 0, 4); body.Controls.Add(rail, 0, 0);
        var main = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8, 0, 12, 0) }; main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 26)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); main.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); main.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); main.Controls.Add(playlistTitle, 0, 0); main.Controls.Add(count, 0, 1); main.Controls.Add(search, 0, 2); tableSurface.Controls.Add(tracks); tableSurface.Controls.Add(empty); main.Controls.Add(tableSurface, 0, 3); var tableActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; tableActions.Controls.Add(up); tableActions.Controls.Add(down); tableActions.Controls.Add(remove); tableActions.Controls.Add(download); main.Controls.Add(tableActions, 0, 4); body.Controls.Add(main, 1, 0);
        var inspector = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(10, 0, 0, 0) }; inspector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); inspector.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); inspector.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); inspector.Controls.Add(StudioTheme.Caption("AGORA EM FOCO", 9, true), 0, 0); inspector.Controls.Add(inspectorTitle, 0, 1); inspector.Controls.Add(inspectorArtist, 0, 2); var inspectorActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; inspectorActions.Controls.Add(edit); inspectorActions.Controls.Add(version); inspector.Controls.Add(inspectorActions, 0, 3);
        var tabs = new StudioTabHost(); var detailsTab = new Panel { Padding = new Padding(4) }; var lyricsTab = new Panel { Padding = new Padding(4) }; detailsTab.Controls.Add(metadata); lyricsTab.Controls.Add(lyrics); tabs.AddPage("Detalhes", detailsTab); tabs.AddPage("Letras", lyricsTab); inspector.Controls.Add(tabs, 0, 4); inspector.Controls.Add(one, 0, 5); body.Controls.Add(inspector, 2, 0);
        var jobs = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, Padding = new Padding(0, 9, 0, 5) }; jobs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); jobs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115)); jobs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150)); jobs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); jobs.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); jobs.RowStyles.Add(new RowStyle(SizeType.Absolute, 7)); jobText.AutoSize = false; jobText.AutoEllipsis = true; jobText.TextAlign = ContentAlignment.MiddleLeft; jobs.Controls.Add(jobText, 0, 0); jobs.Controls.Add(onlyDownload, 1, 0); jobs.Controls.Add(folder, 2, 0); jobs.Controls.Add(cancel, 3, 0); jobs.Controls.Add(progress, 0, 1); jobs.SetColumnSpan(progress, 4); root.Controls.Add(jobs, 0, 2);
        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Padding = new Padding(0, 12, 0, 0) }; transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50)); transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125)); transport.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); transport.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); transport.Controls.Add(nowPlaying, 0, 0); transport.SetColumnSpan(nowPlaying, 4); transport.Controls.Add(clock, 4, 0); transport.Controls.Add(StudioTheme.Caption("VOLUME", 8, true), 5, 0); transport.Controls.Add(previous, 0, 1); transport.Controls.Add(play, 1, 1); transport.Controls.Add(next, 2, 1); transport.Controls.Add(seek, 3, 1); transport.SetColumnSpan(seek, 2); transport.Controls.Add(volume, 5, 1); root.Controls.Add(transport, 0, 3);
    }
    private void ConfigureGrid()
    {
        playlistTitle.AutoSize = inspectorTitle.AutoSize = false; playlistTitle.AutoEllipsis = inspectorTitle.AutoEllipsis = true; playlistTitle.TextAlign = inspectorTitle.TextAlign = ContentAlignment.MiddleLeft;
        playlists.DrawMode = DrawMode.OwnerDrawFixed;
        playlists.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= playlists.Items.Count) return; bool selected = (e.State & DrawItemState.Selected) != 0; using var fill = new SolidBrush(selected ? Color.FromArgb(77, 42, 30) : StudioTheme.Input); e.Graphics.FillRectangle(fill, e.Bounds);
            int gap = (int)(9 * DeviceDpi / 96f); if (selected) { using var accent = new SolidBrush(StudioTheme.Orange); e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, Math.Max(2, (int)(3 * DeviceDpi / 96f)), e.Bounds.Height); }
            TextRenderer.DrawText(e.Graphics, playlists.Items[e.Index].ToString(), playlists.Font, new Rectangle(e.Bounds.X + gap, e.Bounds.Y, Math.Max(0, e.Bounds.Width - gap * 2), e.Bounds.Height), StudioTheme.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding); if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        };
        foreach (var column in new[] { ("#", 7f), ("FAIXA", 37f), ("ARTISTA", 25f), ("TEMPO", 11f), ("ESTADO", 20f) }) tracks.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Item1, FillWeight = column.Item2, SortMode = DataGridViewColumnSortMode.NotSortable });
        tracks.DefaultCellStyle.Padding = new Padding(5); tracks.RowTemplate.Height = 38;
    }
    private void WireActions()
    {
        songNoticeEnabled.CheckedChanged += async (_, _) => { if (!songNoticeEnabled.Checked) songOverlay?.CancelNotification(); SaveUiPreferences(); await UpdateSongWatcher(); };
        testNotice.Click += (_, _) => ShowNoticeDemonstration();
        playlists.SelectedIndexChanged += (_, _) => { if (filling || playlists.SelectedItem is not PlaylistItem value) return; selectedPlaylist = value.Value.Id; selectedTrack = null; RefreshTracks(); };
        tracks.SelectionChanged += async (_, _) => { if (filling) return; selectedTrack = tracks.SelectedRows.Count > 0 ? tracks.SelectedRows[0].Tag as Guid? : null; await RefreshInspector(); UpdateActions(); };
        tracks.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) PlaySelected(); }; search.TextChanged += (_, _) => RefreshTracks();
        create.Click += async (_, _) => { var name = TextPrompt.Ask(this, "Nova playlist", "Nome da playlist", ""); if (name is null) return; await Run("Criando playlist…", _ => Task.Run(() => { var value = service.CreatePlaylist(name); selectedPlaylist = value.Id; })); };
        rename.Click += async (_, _) => { var item = CurrentPlaylist; if (item is null) return; var name = TextPrompt.Ask(this, "Renomear playlist", "Nome da playlist", item.Name); if (name is not null) await Run("Renomeando…", _ => Task.Run(() => service.RenamePlaylist(item.Id, name))); };
        delete.Click += async (_, _) => { var item = CurrentPlaylist; if (item is null) return; if (MessageBox.Show(this, $"Remover “{item.Name}” da biblioteca?\nOs arquivos originais e a exportação no jogo serão preservados.", "Remover playlist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) await Run("Removendo da biblioteca…", _ => Task.Run(() => service.DeletePlaylist(item.Id))); };
        import.Click += async (_, _) => await Import(); edit.Click += async (_, _) => await EditTrack(); remove.Click += async (_, _) => { var item = CurrentTrack; if (item is not null && selectedPlaylist is Guid id) await Run("Removendo faixa…", _ => Task.Run(() => service.RemoveTrack(id, item.Id))); };
        up.Click += async (_, _) => await Reorder(-1); down.Click += async (_, _) => await Reorder(1);
        tools.Click += async (_, _) => await Run("Preparando ferramentas portáteis…", async token => { var result = await service.EnsureToolsAsync(Reporter(), token); SetStatus(result.Ready ? "Ferramentas prontas: yt-dlp, FFmpeg e runtime JavaScript local." : "Ferramentas pendentes: " + string.Join(", ", result.Missing)); });
        download.Click += async (_, _) => { if (selectedPlaylist is Guid id) await Run("Baixando e preparando playlist…", async token => { ReleaseAudioForPlaylist(id); var result = await service.ProcessPlaylistAsync(id, Reporter(), token); token.ThrowIfCancellationRequested(); if (result.Failed > 0 || result.Cancelled > 0) { SetStatus($"Preparadas: {result.Succeeded}; falhas: {result.Failed}; canceladas: {result.Cancelled}. Playlist não carregada: todas as faixas precisam estar prontas. " + string.Join(" ", result.Warnings)); return; } string? executable = takedawnExecutable; var activated = await Task.Run(() => service.ActivatePlaylistAsync(id, gameConfigurationPath, null, token, executable), token); SetStatus(activated.Message + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : "")); }); };
        onlyDownload.Click += async (_, _) => { if (selectedPlaylist is Guid id) await Run("Preparando playlist sem instalar…", async token => { ReleaseAudioForPlaylist(id); ShowOperation(await service.ProcessPlaylistAsync(id, Reporter(), token)); }); };
        one.Click += async (_, _) => { if (CurrentTrack is Track track && selectedPlaylist is Guid id) await Run("Preparando faixa…", async token => ShowOperation(await service.ProcessTrackAsync(id, track.Id, Reporter(), token))); };
        version.Click += async (_, _) => await ChooseVersion();
        install.Click += async (_, _) => await ActivateSelected(); folder.Click += async (_, _) => await LocateTakedawn(); updateGame.Click += (_, _) => { try { Process.Start(new ProcessStartInfo("https://github.com/xenophobentx/takedawn/releases/latest") { UseShellExecute = true }); } catch (Exception ex) { SetStatus("Não foi possível abrir a página oficial: " + ex.Message); } }; cancel.Click += (_, _) => { operation?.Cancel(); SetStatus("Cancelamento solicitado. Aguardando a operação encerrar…"); };
        play.Click += (_, _) => { try { if (audio.HasTrack && playingTrack == CurrentTrack?.Id) { if (audio.IsPlaying) { audio.Pause(); play.Text = "▶"; } else { audio.Play(); play.Text = "Ⅱ"; } } else PlaySelected(); } catch (Exception ex) { SetStatus(ex.Message); } };
        previous.Click += (_, _) => Skip(-1); next.Click += (_, _) => Skip(1); volume.ValueChanged += (_, _) => { try { audio.Volume(volume.Value); } catch (Exception ex) { SetStatus(ex.Message); } };
        seek.Scroll += (_, _) => { try { if (audio.HasTrack) audio.Seek((int)((long)TransportLength() * seek.Value / 1000)); } catch (Exception ex) { SetStatus(ex.Message); } };
        timer.Tick += (_, _) => TickAudio();
        tracks.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; PlaySelected(); } else if (e.KeyCode == Keys.F2) { e.Handled = true; await EditTrack(); } };
    }
    private Playlist? CurrentPlaylist => library.Playlists.FirstOrDefault(x => x.Id == selectedPlaylist);
    private Track? CurrentTrack => CurrentPlaylist?.Tracks.FirstOrDefault(x => x.Id == selectedTrack);
    private void RefreshLibrary()
    {
        library = service.GetLibrary(); var toolStatus = service.GetTools(); tools.Text = toolStatus.Ready ? "Ferramentas prontas" : "Preparar ferramentas"; tips.SetToolTip(tools, toolStatus.Ready ? "Ferramentas portáteis disponíveis. Clique para verificar." : "Preparar localmente: " + string.Join(", ", toolStatus.Missing)); create.BackColor = library.Playlists.Count == 0 ? StudioTheme.Orange : StudioTheme.Input; create.ForeColor = library.Playlists.Count == 0 ? StudioTheme.Asphalt : StudioTheme.White; filling = true; playlists.Items.Clear(); foreach (var value in library.Playlists) playlists.Items.Add(new PlaylistItem(value, false));
        if (!library.Playlists.Any(x => x.Id == selectedPlaylist)) selectedPlaylist = library.Playlists.FirstOrDefault()?.Id;
        for (int i = 0; i < playlists.Items.Count; i++) if (((PlaylistItem)playlists.Items[i]).Value.Id == selectedPlaylist) playlists.SelectedIndex = i;
        if (audio.HasTrack && library.Playlists.SelectMany(x => x.Tracks).FirstOrDefault(x => x.Id == playingTrack) is { } active) nowPlaying.Text = $"{active.Title}  /  {active.Artist}";
        filling = false; RefreshTracks(); _ = RefreshActivation();
    }
    private void RefreshTracks()
    {
        var playlist = CurrentPlaylist; playlistTitle.Text = playlist?.Name ?? "Sua próxima trilha começa aqui"; RefreshPlaylistSummary();
        int scroll = tracks.FirstDisplayedScrollingRowIndex; filling = true; tracks.Rows.Clear();
        if (playlist is not null)
            for (int i = 0; i < playlist.Tracks.Count; i++)
            {
                var item = playlist.Tracks[i]; if (!string.IsNullOrWhiteSpace(search.Text) && !($"{item.Title} {item.Artist} {item.Album}").Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)) continue;
                int row = tracks.Rows.Add((i + 1).ToString("00"), item.Title, item.Artist, Duration(item.DurationSeconds), item.MetadataDirty ? "Dados alterados" : Status(item.Status)); tracks.Rows[row].Tag = item.Id;
                if (item.Status == TrackStatus.Failed) tracks.Rows[row].Cells[4].Style.ForeColor = Color.Salmon;
            }
        tracks.ClearSelection();
        foreach (DataGridViewRow row in tracks.Rows) if (row.Tag is Guid id && id == selectedTrack) { row.Selected = true; tracks.CurrentCell = row.Cells[1]; break; }
        if (tracks.SelectedRows.Count == 0 && tracks.Rows.Count > 0) { tracks.Rows[0].Selected = true; selectedTrack = (Guid)tracks.Rows[0].Tag!; }
        if (tracks.Rows.Count == 0) selectedTrack = null;
        if (scroll >= 0 && scroll < tracks.Rows.Count) tracks.FirstDisplayedScrollingRowIndex = scroll;
        empty.Visible = tracks.Rows.Count == 0; empty.Text = playlist is { Tracks.Count: > 0 } ? "NENHUMA FAIXA NESTE FILTRO.\n\nAltere a busca para rever sua playlist." : "NENHUMA FAIXA NA PISTA.\n\nCrie uma playlist e importe seus MP3s.\nVocê também pode montar a lista com texto,\nYouTube ou uma playlist pública do Spotify."; if (empty.Visible) empty.BringToFront(); filling = false; _ = RefreshInspector(); UpdateActions();
    }
    private async Task RefreshInspector()
    {
        int generation = ++inspectorGeneration; var item = CurrentTrack;
        inspectorTitle.Text = item?.Title ?? "Selecione uma faixa"; inspectorArtist.Text = item?.Artist ?? "";
        string displaySource = item is null ? "" : item.YouTubeUrl ?? (File.Exists(item.Source) ? Path.GetFileName(item.Source) : item.Source);
        metadata.Text = item is null ? "Revise título, artista e ordem antes de levar sua música ao jogo.\r\n\r\nA biblioteca começa vazia; nenhuma faixa é baixada sem sua ação." : $"Álbum: {item.Album}\r\nDuração: {Duration(item.DurationSeconds)}\r\nEstado: {Status(item.Status)}{(item.MetadataDirty ? " — dados editados; prepare novamente" : "")}\r\n\r\nFonte:\r\n{displaySource}\r\n\r\nMP3:\r\n{(item.LocalPath is null ? "Ainda não preparado" : Path.GetFileName(item.LocalPath))}\r\n\r\n{item.Error}";
        tips.SetToolTip(metadata, item?.LocalPath ?? item?.Source ?? "");
        lyrics.Text = LyricsLabel(item);
        if (item?.LyricsPath is string file && File.Exists(file))
        {
            try { string text = await File.ReadAllTextAsync(file); if (!IsDisposed && generation == inspectorGeneration) lyrics.Text = text + "\n\n— Letras neste aplicativo e nos sidecars; sem overlay no jogo."; }
            catch (Exception ex) { if (!IsDisposed && generation == inspectorGeneration) lyrics.Text = "Não foi possível ler a letra: " + ex.Message; }
        }
    }
    private static string LyricsLabel(Track? item) => item is null ? "Selecione uma faixa para ver a letra.\n\nAs letras aparecem aqui e nos arquivos sidecar, não dentro do jogo." : item.LyricsStatus switch { LyricsStatus.Instrumental => "Faixa identificada como instrumental.", LyricsStatus.NotFound => "Nenhuma letra foi encontrada.\n\nConfira título e artista e tente processar a faixa novamente.", LyricsStatus.Failed => "A consulta de letra falhou.\n" + item.LyricsError, _ => "A letra será consultada durante o processamento da faixa.\n\nAs letras ficam neste aplicativo e em sidecars; o jogo não as exibe." };
    private void UpdateActions()
    {
        bool hasPlaylist = CurrentPlaylist is not null, hasTrack = CurrentTrack is not null;
        create.Enabled = tools.Enabled = updateGame.Enabled = !busy; rename.Enabled = delete.Enabled = import.Enabled = !busy && hasPlaylist;
        download.Enabled = onlyDownload.Enabled = !busy && CurrentPlaylist is { Tracks.Count: > 0 };
        install.Enabled = !busy && CurrentPlaylist is { Tracks.Count: > 0 } selected && selected.Tracks.All(IsPrepared);
        tips.SetToolTip(install, install.Enabled ? "Configura somente esta playlist para a próxima execução do jogo." : "Adicione faixas e prepare todas elas antes de carregar a playlist.");
        edit.Enabled = remove.Enabled = up.Enabled = down.Enabled = version.Enabled = one.Enabled = !busy && hasTrack; folder.Enabled = !busy; cancel.Enabled = busy;
        previous.Enabled = next.Enabled = hasPlaylist; play.Enabled = hasTrack || audio.HasTrack;
    }
    private async Task Run(string message, Func<CancellationToken, Task> action)
    {
        if (busy) return; busy = true; operationRevision++; operation = new CancellationTokenSource(); progress.Style = ProgressBarStyle.Marquee; SetStatus(message); UpdateActions();
        try { await action(operation.Token); if (jobText.Text == message) SetStatus("Concluído. Alterações salvas na biblioteca; carregue novamente a playlist para refletir mudanças no jogo."); }
        catch (OperationCanceledException) { SetStatus("Operação cancelada. Os itens já concluídos foram preservados; você pode tentar novamente."); }
        catch (Exception ex) { SetStatus("Não foi possível concluir: " + ex.Message); }
        finally { busy = false; operation.Dispose(); operation = null; progress.Style = ProgressBarStyle.Continuous; progress.Value = 0; RefreshLibrary(); UpdateActions(); if (closeAfterOperation) BeginInvoke(Close); }
    }
    private IProgress<OperationProgress> Reporter()
    {
        int revision = operationRevision;
        return new Progress<OperationProgress>(value => { if (IsDisposed || !busy || revision != operationRevision) return; SetStatus(value.Message); if (value.Percent is double percent) { progress.Style = ProgressBarStyle.Continuous; progress.Value = Math.Clamp((int)percent, 0, 100); } else progress.Style = ProgressBarStyle.Marquee; library = service.GetLibrary(); foreach (DataGridViewRow row in tracks.Rows) if (row.Tag is Guid id && id == value.TrackId) { var item = CurrentPlaylist?.Tracks.FirstOrDefault(x => x.Id == id); if (item is not null) row.Cells[4].Value = Status(item.Status); } });
    }
    private void ShowOperation(OperationResult result) => SetStatus($"Concluídas: {result.Succeeded}  ·  Falhas: {result.Failed}  ·  Canceladas: {result.Cancelled}" + (result.Warnings.Count > 0 ? "\n" + string.Join("\n", result.Warnings) : ""));
    private void SetStatus(string text) { jobText.Text = text.Replace('\r', ' ').Replace('\n', ' '); tips.SetToolTip(jobText, text); }
    private async Task Import()
    {
        if (CurrentPlaylist is not Playlist playlist) return;
        using var dialog = new ImportDialog(lastImportMode, lastImportText, lastImportFiles); if (dialog.ShowDialog(this) != DialogResult.OK) return;
        lastImportMode = dialog.Mode; lastImportText = dialog.Input; lastImportFiles = dialog.Files;
        var mode = dialog.Mode; string input = dialog.Input; string[] localFiles = dialog.Files;
        await Run("Importando para a biblioteca…", async token =>
        {
            if (mode == ImportMode.Local)
            {
                int added = 0;
                for (int i = 0; i < localFiles.Length; i++) { token.ThrowIfCancellationRequested(); added += (await Task.Run(() => service.ImportLocalFiles(playlist.Id, [localFiles[i]]), token)).Count; lastImportFiles = localFiles.Skip(i + 1).ToArray(); progress.Style = ProgressBarStyle.Continuous; progress.Value = (i + 1) * 100 / localFiles.Length; SetStatus($"{i + 1}/{localFiles.Length} arquivos adicionados à lista."); }
                SetStatus($"{added} MP3s adicionados à lista. Você já pode ouvir a prévia original; Baixar e carregar prepara a playlist para o jogo."); return;
            }
            ImportResult result = mode switch { ImportMode.YouTube => await service.ImportYouTubePlaylistAsync(playlist.Id, input, token), ImportMode.Spotify => await service.ImportSpotifyAsync(playlist.Id, input, null, token), ImportMode.Csv => await Task.Run(() => service.ImportCsv(playlist.Id, input), token), _ => await Task.Run(() => service.ImportText(playlist.Id, input), token) };
            SetStatus($"{result.Added} faixas importadas. {(result.Complete ? "Lista importada conforme a fonte." : "Importação parcial ou não disponível; use CSV ou texto para completar.")}" + (result.Warnings.Count > 0 ? " " + string.Join(" ", result.Warnings) : ""));
        });
    }
    private async Task EditTrack()
    {
        var item = CurrentTrack; if (busy || item is null || selectedPlaylist is not Guid id) return;
        using var dialog = new MetadataDialog(item.Title, item.Artist, item.Album); if (dialog.ShowDialog(this) == DialogResult.OK) { string title = dialog.TrackTitle, artist = dialog.Artist, album = dialog.Album; await Run("Atualizando dados…", _ => Task.Run(() => service.UpdateTrack(id, item.Id, title, artist, album))); }
    }
    private async Task Reorder(int delta) { var playlist = CurrentPlaylist; if (playlist is null || CurrentTrack is not Track track) return; int index = playlist.Tracks.FindIndex(x => x.Id == track.Id); int target = Math.Clamp(index + delta, 0, playlist.Tracks.Count - 1); if (target != index) await Run("Atualizando ordem…", _ => Task.Run(() => service.ReorderTrack(playlist.Id, track.Id, target))); }
    private async Task ChooseVersion()
    {
        var track = CurrentTrack; if (track is null || selectedPlaylist is not Guid playlistId) return;
        await Run("Buscando versões no YouTube…", async token => { var matches = await service.SearchYouTubeAsync($"{track.Artist} {track.Title}", token); using var dialog = new CandidateDialog(matches); if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Selected is { } candidate) { service.SelectCandidate(playlistId, track.Id, candidate); SetStatus("Versão selecionada. Confira os dados e prepare a faixa quando quiser baixar."); } });
    }
    private async Task ActivateSelected()
    {
        if (selectedPlaylist is not Guid id) return;
        await Run("Carregando somente a playlist selecionada…", async token => { string? executable = takedawnExecutable; var result = await Task.Run(() => service.ActivatePlaylistAsync(id, gameConfigurationPath, null, token, executable), token); SetStatus(result.Message); });
    }
    private async Task LocateTakedawn()
    {
        using var dialog = new OpenFileDialog { Title = "Localizar Takedawn.exe — apenas identificar versão", Filter = "Executável do jogo|Takedawn.exe", CheckFileExists = true };
        if (takedawnExecutable is string current && File.Exists(current)) dialog.InitialDirectory = Path.GetDirectoryName(current);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!Path.GetFileName(dialog.FileName).Equals("Takedawn.exe", StringComparison.OrdinalIgnoreCase)) { SetStatus("Selecione o Takedawn.exe do jogo, não um instalador ou outro programa."); return; }
        takedawnExecutable = dialog.FileName; await RefreshActivation();
        if (SaveUiPreferences()) SetStatus(compatibility?.Message ?? "Executável selecionado; versão não identificada.");
    }
    private async Task RefreshActivation()
    {
        int revision = ++activationRevision; string? executable = takedawnExecutable; activationVerifying = true; RefreshPlaylistSummary();
        try
        {
            var result = await Task.Run(() => (Compatibility: service.DetectTakedawn(executable), Active: service.GetActiveStatus(gameConfigurationPath)));
            if (IsDisposed || revision != activationRevision) return; compatibility = result.Compatibility; activeStatus = result.Active;
        }
        catch (Exception ex) { if (IsDisposed || revision != activationRevision) return; compatibility = null; activeStatus = null; tips.SetToolTip(setup, "Não foi possível verificar a ativação: " + ex.Message); }
        if (IsDisposed || revision != activationRevision) return;
        activationVerifying = false;
        string installed = compatibility?.InstalledVersion?.ToString() ?? "não identificado";
        setup.Text = $"TAKEDAWN {installed}\n{(compatibility?.AutomaticSupported == true ? "Seleção automática suportada" : "Manual X/A · auto requer 0.2.1+")}";
        tips.SetToolTip(setup, (compatibility?.Message ?? "Localize o executável para identificar a versão.") + "\n" + (activeStatus?.Message ?? "Nenhuma playlist configurada."));
        updateGame.Visible = compatibility?.AutomaticSupported != true;
        tips.SetToolTip(updateGame, "Seleção automática exige Takedawn 0.2.1+. Abre a página oficial de versões; não instala nada.");
        int selected = playlists.SelectedIndex; filling = true;
        for (int i = 0; i < playlists.Items.Count; i++) if (playlists.Items[i] is PlaylistItem item) playlists.Items[i] = item with { Configured = activeStatus is { ConfigMatches: true, FilesReady: true } active && active.PlaylistId == item.Value.Id };
        playlists.SelectedIndex = selected; filling = false; RefreshPlaylistSummary();
    }
    private void RefreshPlaylistSummary()
    {
        if (IsDisposed) return; var playlist = CurrentPlaylist;
        count.Text = playlist is null ? "Crie uma playlist para começar" : $"{playlist.Tracks.Count} faixas  ·  {playlist.Tracks.Count(IsPrepared)} prontas  ·  {(activationVerifying ? "Verificando ativação…" : ActivationLabel(playlist))}";
        count.ForeColor = !activationVerifying && playlist is not null && activeStatus?.PlaylistId == playlist.Id && activeStatus.ConfigMatches && activeStatus.FilesReady ? StudioTheme.Orange : StudioTheme.Muted; tips.SetToolTip(count, activeStatus?.Message ?? "Nenhuma playlist configurada pelo aplicativo.");
    }
    private string ActivationLabel(Playlist playlist)
    {
        if (activeStatus?.PlaylistId != playlist.Id || !activeStatus.ConfigMatches) return "Não configurada";
        if (!activeStatus.FilesReady || playlist.Tracks.Count == 0 || !playlist.Tracks.All(IsPrepared)) return "Atualização pendente";
        if (activeStatus.AutomaticSupported && compatibility?.AutomaticSupported != true) return "Versão alterada · recarregue";
        return activeStatus.AutomaticSupported ? "Configurada · próxima execução" : "Configurada · manual X/A";
    }
    private static bool IsPrepared(Track track) => track.Status == TrackStatus.Downloaded && !track.MetadataDirty && track.LocalPath is string path && File.Exists(path);
    private void PlaySelected()
    {
        var track = CurrentTrack; string? file = track is null ? null : PreviewFile(track); if (track is null || file is null) { SetStatus("Esta faixa ainda não tem um MP3 local. Prepare a faixa ou importe um arquivo."); return; }
        try { audio.Open(file); audio.Volume(volume.Value); audio.Play(); playingTrack = track.Id; playingPlaylist = selectedPlaylist; nowPlaying.Text = $"{track.Title}  /  {track.Artist}{(track.LocalPath is null ? "  /  PRÉVIA ORIGINAL" : "")}"; play.Text = "Ⅱ"; } catch (Exception ex) { SetStatus(ex.Message); }
    }
    private void Skip(int direction)
    {
        var playlist = library.Playlists.FirstOrDefault(x => x.Id == (playingPlaylist ?? selectedPlaylist)); var available = playlist?.Tracks.Where(x => PreviewFile(x) is not null).ToList(); if (available is null || available.Count == 0) return;
        int index = available.FindIndex(x => x.Id == playingTrack); int target = (index + direction + available.Count) % available.Count; selectedPlaylist = playlist!.Id; selectedTrack = available[target].Id; RefreshLibrary(); PlaySelected();
    }
    private void TickAudio()
    {
        if (!audio.HasTrack) return; int length = TransportLength(), position = audio.Position; if (!seek.Capture) seek.Value = length > 0 ? Math.Clamp((int)((long)position * 1000 / length), 0, 1000) : 0; clock.Text = $"{Duration(position / 1000d)} / {Duration(length / 1000d)}";
        if (audio.Ended) { audio.Stop(); play.Text = "▶"; var available = library.Playlists.FirstOrDefault(x => x.Id == playingPlaylist)?.Tracks.Where(x => PreviewFile(x) is not null).ToList(); if (available is not null && available.FindIndex(x => x.Id == playingTrack) < available.Count - 1) Skip(1); }
    }
    private static string? PreviewFile(Track value) => value.LocalPath is string local && File.Exists(local) ? local : Path.GetExtension(value.Source).Equals(".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(value.Source) ? value.Source : null;
    private int TransportLength() { double? measured = library.Playlists.SelectMany(x => x.Tracks).FirstOrDefault(x => x.Id == playingTrack)?.DurationSeconds; return measured is > 0 && measured < int.MaxValue / 1000d ? (int)Math.Round(measured.Value * 1000) : audio.Length; }
    private void ReleaseAudioForPlaylist(Guid id) { if (playingPlaylist != id) return; audio.Close(); playingTrack = null; play.Text = "▶"; nowPlaying.Text = "REPRODUÇÃO PAUSADA PARA PREPARAR OS ARQUIVOS"; }
    private static string Duration(double? seconds) { if (seconds is null || seconds < 0) return "—"; var time = TimeSpan.FromSeconds(seconds.Value); return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss"); }
    private static string Status(TrackStatus status) => status switch { TrackStatus.Added => "Na lista", TrackStatus.Resolving => "Buscando versão", TrackStatus.Ready => "Pronta para baixar", TrackStatus.Downloading => "Baixando", TrackStatus.Downloaded => "MP3 disponível", TrackStatus.Failed => "Falhou", TrackStatus.Cancelled => "Cancelada", _ => status.ToString() };
    private static Button Button(string text, int width) => new StudioButton { Text = text, Width = width, Height = 32, Margin = new Padding(3), AutoSize = false };
    private bool SaveUiPreferences()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(uiSettingsPath)!); File.WriteAllText(uiSettingsPath, JsonSerializer.Serialize(new UiSettings(takedawnExecutable, songNoticeEnabled.Checked))); return true; }
        catch (Exception ex) { SetStatus("Preferência mantida nesta sessão; não foi possível salvar os ajustes locais: " + ex.Message); return false; }
    }
    internal void EnableSongNoticeFromLauncher() => DispatchNotification(() =>
    {
        if (!songNoticeEnabled.Checked) songNoticeEnabled.Checked = true;
        else { SaveUiPreferences(); _ = UpdateSongWatcher(); }
    });
    private void ShowNoticeDemonstration()
    {
        if (notificationClosing) return;
        songOverlay ??= new NowPlayingOverlay(Handle);
        bool visible = songOverlay.ShowDemonstration();
        SetStatus(visible ? "DEMONSTRAÇÃO do aviso por 6 segundos. Nenhum jogo foi iniciado; o gerenciador precisa permanecer aberto para os avisos reais." : "Demonstração não exibida: mantenha a janela do gerenciador visível e em primeiro plano e clique em Testar aviso.");
    }
    private async Task UpdateSongWatcher()
    {
        if (notificationClosing || IsDisposed) return;
        try { await watcherGate.WaitAsync(); } catch (ObjectDisposedException) { return; }
        try
        {
            if (!songNoticeEnabled.Checked || notificationClosing) { await StopSongWatcher(); if (!IsDisposed) watcherState.Text = "Aviso desativado"; return; }
            if (songWatcher is not null) return;
            watcherState.Text = "Preparando detector…";
            watcherCancellation = new CancellationTokenSource();
            var watcher = new NowPlayingWatcher(service); songWatcher = watcher;
            watcher.TrackChanged += OnGameTrackChanged; watcher.StatusChanged += OnWatcherStatusChanged;
            CancellationToken token = watcherCancellation.Token;
            watcherRun = Task.Run(async () =>
            {
                try { await watcher.StartAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex) { DispatchNotification(() => { if (ReferenceEquals(songWatcher, watcher)) { watcherState.Text = "Detector indisponível"; tips.SetToolTip(watcherState, ex.Message); } }); }
            });
        }
        catch (Exception ex) { if (!IsDisposed) { watcherState.Text = "Detector indisponível"; tips.SetToolTip(watcherState, ex.Message); } }
        finally { try { watcherGate.Release(); } catch (ObjectDisposedException) { } }
    }
    private async Task StopSongWatcher()
    {
        var watcher = songWatcher; songWatcher = null; watcherCancellation?.Cancel();
        if (watcher is not null) { watcher.TrackChanged -= OnGameTrackChanged; watcher.StatusChanged -= OnWatcherStatusChanged; try { await watcher.DisposeAsync(); } catch (OperationCanceledException) { } }
        if (watcherRun is not null) { try { await watcherRun; } catch (OperationCanceledException) { } watcherRun = null; }
        watcherCancellation?.Dispose(); watcherCancellation = null;
    }
    private void OnGameTrackChanged(object? sender, TrackNotification notification) => DispatchNotification(() =>
    {
        if (!songNoticeEnabled.Checked || !ReferenceEquals(sender, songWatcher)) return;
        songOverlay ??= new NowPlayingOverlay(Handle);
        songOverlay.ShowTrack(notification.Title, notification.Artist, notification.ProcessId);
    });
    private void OnWatcherStatusChanged(object? sender, WatcherStatus status) => DispatchNotification(() =>
    {
        if (!songNoticeEnabled.Checked || !ReferenceEquals(sender, songWatcher)) return;
        watcherState.Text = status.Available ? "Detecção disponível" : "Detector indisponível"; tips.SetToolTip(watcherState, status.Message);
    });
    private void DispatchNotification(Action action)
    {
        if (IsDisposed || notificationClosing || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed && !notificationClosing) action(); })); }
        catch (InvalidOperationException) { }
    }
    private async Task StopNotificationsAndClose()
    {
        if (notificationClosing) return; notificationClosing = true; songNoticeEnabled.Enabled = testNotice.Enabled = false; songOverlay?.Dispose(); songOverlay = null;
        await watcherGate.WaitAsync();
        try { await StopSongWatcher(); }
        catch (Exception) { /* Notification cleanup must not prevent the manager closing. */ }
        finally { watcherGate.Release(); notificationCleanupReady = true; if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)Close); }
    }
    private sealed record UiSettings(string? TakedawnExecutable, bool SongNoticeEnabled = true);
    private sealed record PlaylistItem(Playlist Value, bool Configured) { public override string ToString() => $"{(Configured ? "● " : "")}{Value.Name}  ·  {Value.Tracks.Count}"; }
}
