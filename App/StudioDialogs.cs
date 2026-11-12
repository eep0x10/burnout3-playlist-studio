using Burnout3.PlaylistStudio.Core;

namespace Burnout3.PlaylistStudio.App;

internal static class TextPrompt
{
    internal static string? Ask(IWin32Window owner, string title, string caption, string initial)
    {
        using var form = new Form(); form.SuspendLayout(); form.AutoScaleMode = AutoScaleMode.Dpi; form.AutoScaleDimensions = new SizeF(96, 96); form.Font = new Font("Segoe UI", 10); form.Text = title; form.ClientSize = new Size(500, 190); form.StartPosition = FormStartPosition.CenterParent; form.MinimizeBox = form.MaximizeBox = false; form.FormBorderStyle = FormBorderStyle.FixedDialog;
        var input = new TextBox { Text = initial, Dock = DockStyle.Fill, MaxLength = 100, AccessibleName = caption };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); root.Controls.Add(StudioTheme.Caption(caption), 0, 0); root.Controls.Add(input, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft }; var okay = new StudioButton { Text = "Salvar", Size = new Size(110, 32) }; var cancel = new StudioButton { Text = "Cancelar", Size = new Size(110, 32), DialogResult = DialogResult.Cancel }; actions.Controls.Add(okay); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2); form.Controls.Add(root); form.CancelButton = cancel; form.AcceptButton = okay;
        okay.Click += (_, _) => { if (string.IsNullOrWhiteSpace(input.Text)) { input.Focus(); return; } form.DialogResult = DialogResult.OK; form.Close(); }; form.Shown += (_, _) => { StudioTheme.DarkTitle(form); input.SelectAll(); input.Focus(); }; StudioTheme.Apply(form); form.ResumeLayout(true);
        return form.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }
}

internal sealed class MetadataDialog : Form
{
    private readonly TextBox title = new() { Dock = DockStyle.Fill }, artist = new() { Dock = DockStyle.Fill }, album = new() { Dock = DockStyle.Fill };
    internal string TrackTitle => title.Text.Trim(); internal string Artist => artist.Text.Trim(); internal string Album => album.Text.Trim();
    internal MetadataDialog(string currentTitle, string currentArtist, string currentAlbum)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Editar faixa"; ClientSize = new Size(560, 300); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        title.Text = currentTitle; artist.Text = currentArtist; album.Text = currentAlbum;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 5 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90)); root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); Controls.Add(root);
        Control[] controls = [title, artist, album]; string[] names = ["Título", "Artista", "Álbum"];
        for (int i = 0; i < controls.Length; i++) { root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); root.Controls.Add(StudioTheme.Caption(names[i]), 0, i); controls[i].AccessibleName = names[i]; root.Controls.Add(controls[i], 1, i); }
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        var note = StudioTheme.Caption("O jogo exibe até 31 caracteres do título. Seus dados completos permanecem na biblioteca.", 9); root.Controls.Add(note, 0, 3); root.SetColumnSpan(note, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft }; var save = new StudioButton { Text = "Salvar dados", Size = new Size(140, 34) }; var cancel = new StudioButton { Text = "Cancelar", Size = new Size(110, 34), DialogResult = DialogResult.Cancel }; actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4); root.SetColumnSpan(actions, 2); CancelButton = cancel;
        save.Click += (_, _) => { if (string.IsNullOrWhiteSpace(title.Text)) { title.Focus(); return; } DialogResult = DialogResult.OK; Close(); }; Shown += (_, _) => StudioTheme.DarkTitle(this); StudioTheme.Apply(this); ResumeLayout(true);
    }
}

internal enum ImportMode { Text, YouTube, Spotify, Local, Csv }
internal sealed class PlaylistSourceDialog : Form
{
    private readonly TextBox url = new() { Dock = DockStyle.Fill, AccessibleName = "URL da playlist de origem" };
    private readonly TextBox token = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, AccessibleName = "Token da API Spotify — somente nesta sessão" };
    internal string SourceUrl => url.Text.Trim();
    internal string AccessToken => token.Text.Trim();
    internal PlaylistSourceDialog(string? sourceUrl, string? accessToken)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Fonte da playlist"; ClientSize = new Size(650, 350); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        url.Text = sourceUrl ?? ""; token.Text = accessToken ?? "";
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 26, 42, 32, 42, 105, 48 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        root.Controls.Add(StudioTheme.Caption("URL do YouTube ou Spotify · deixe vazia para desvincular"), 0, 0); root.Controls.Add(url, 0, 1);
        root.Controls.Add(StudioTheme.Caption("Token da API Spotify · opcional, somente nesta sessão", 9), 0, 2); root.Controls.Add(token, 0, 3);
        root.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Salvar consulta a origem e prepara as faixas automaticamente.\nSpotify público pode ser parcial; nesse caso não remove faixas ausentes. Um token autorizado pode ampliar o acesso. O token não será salvo em arquivo.\nPara remover músicas da origem, edite a playlist no YouTube/Spotify.", AutoSize = false }, 0, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new StudioButton { Text = "Salvar e atualizar", Width = 160, Height = 34 };
        var cancel = new StudioButton { Text = "Cancelar", Width = 110, Height = 34, DialogResult = DialogResult.Cancel };
        actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 5); Controls.Add(root); CancelButton = cancel; AcceptButton = save;
        save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); }; Shown += (_, _) => StudioTheme.DarkTitle(this); StudioTheme.Apply(this); ResumeLayout(true);
    }
    protected override void Dispose(bool disposing) { if (disposing) token.Clear(); base.Dispose(disposing); }
}
internal sealed class ImportDialog : Form
{
    private readonly StudioTabHost tabs = new();
    private readonly TextBox text = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Artista - Título\r\nArtista - Título\r\nhttps://www.youtube.com/watch?v=…" }, youtube = new() { Dock = DockStyle.Top, PlaceholderText = "https://www.youtube.com/playlist?list=…" }, spotify = new() { Dock = DockStyle.Top, PlaceholderText = "https://open.spotify.com/playlist/…" };
    private readonly ListBox files = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    private bool csv;
    internal ImportMode Mode => tabs.SelectedIndex switch { 1 => ImportMode.YouTube, 2 => ImportMode.Spotify, 3 => ImportMode.Local, _ => csv ? ImportMode.Csv : ImportMode.Text };
    internal string Input => Mode switch { ImportMode.YouTube => youtube.Text.Trim(), ImportMode.Spotify => spotify.Text.Trim(), _ => text.Text };
    internal string[] Files => files.Items.Cast<string>().ToArray();
    internal ImportDialog(ImportMode previous = ImportMode.Text, string draft = "", string[]? localFiles = null)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Adicionar à playlist"; ClientSize = new Size(720, 540); MinimumSize = new Size(640, 440); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(root); root.Controls.Add(StudioTheme.Caption("ESCOLHA A FONTE. CONFIRA A VERSÃO. MONTE SUA TRILHA.", 11, true), 0, 0); root.Controls.Add(tabs, 0, 1);
        var textPage = Page("Texto / CSV", "Uma faixa ou URL por linha. Use Artista - Título. CSV pode conter colunas de título e artista."); var csvButton = new StudioButton { Text = "Abrir CSV…", Dock = DockStyle.Bottom, Height = 34 }; textPage.Controls.Add(text); textPage.Controls.Add(csvButton);
        var ytPage = Page("YouTube", "Cole uma URL de playlist. A origem ficará vinculada. As faixas serão preparadas automaticamente; Atualizar consulta novas músicas e remoções."); ytPage.Controls.Add(youtube);
        var spotifyPage = Page("Spotify", "Importação pública best-effort. Spotify pode limitar ou negar acesso. A contagem e as limitações serão mostradas; use CSV ou texto para completar. Nenhuma credencial será armazenada."); spotifyPage.Controls.Add(spotify);
        var localPage = Page("MP3 locais", "Adicione MP3s à lista sem alterar os originais. A preparação automática cria as cópias da biblioteca; seus arquivos originais serão preservados."); var browse = new StudioButton { Text = "Escolher MP3s…", Dock = DockStyle.Bottom, Height = 34 }; localPage.Controls.Add(files); localPage.Controls.Add(browse);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0), WrapContents = false }; var okay = new StudioButton { Text = "Adicionar e preparar", Size = new Size(190, 34) }; var cancel = new StudioButton { Text = "Cancelar", Size = new Size(110, 34), DialogResult = DialogResult.Cancel }; actions.Controls.Add(okay); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2); CancelButton = cancel;
        okay.Click += (_, _) => { if (Mode == ImportMode.Local ? Files.Length == 0 : string.IsNullOrWhiteSpace(Input)) return; DialogResult = DialogResult.OK; Close(); };
        csvButton.Click += async (_, _) => { using var dialog = new OpenFileDialog { Filter = "CSV|*.csv|Todos os arquivos|*.*", Title = "Importar CSV" }; if (dialog.ShowDialog(this) != DialogResult.OK) return; try { text.Text = await File.ReadAllTextAsync(dialog.FileName); csv = true; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Importar CSV"); } };
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "MP3|*.mp3", Multiselect = true, Title = "Importar MP3s locais" }; if (dialog.ShowDialog(this) == DialogResult.OK) { var values = Files.Concat(dialog.FileNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); files.Items.Clear(); files.Items.AddRange(values); } };
        tabs.SelectedIndex = previous switch { ImportMode.YouTube => 1, ImportMode.Spotify => 2, ImportMode.Local => 3, _ => 0 }; csv = previous == ImportMode.Csv; if (previous == ImportMode.YouTube) youtube.Text = draft; else if (previous == ImportMode.Spotify) spotify.Text = draft; else text.Text = draft; if (localFiles is not null) files.Items.AddRange(localFiles);
        Shown += (_, _) => StudioTheme.DarkTitle(this); StudioTheme.Apply(this); ResumeLayout(true);
    }
    private Panel Page(string title, string explanation) { var tab = new Panel { Padding = new Padding(12) }; tabs.AddPage(title, tab); var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 }; layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, explanation.Length > 200 ? 100 : 64)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); tab.Controls.Add(layout); layout.Controls.Add(new Label { Text = explanation, AutoSize = false, Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 14) }, 0, 0); var content = new Panel { Dock = DockStyle.Fill }; layout.Controls.Add(content, 0, 1); return content; }
}

internal sealed class CandidateDialog : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill, HorizontalScrollbar = true };
    internal YouTubeCandidate? Selected => (list.SelectedItem as Item)?.Value;
    internal CandidateDialog(IReadOnlyList<YouTubeCandidate> candidates)
    {
        SuspendLayout(); AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96); Font = new Font("Segoe UI", 10); Text = "Escolher versão no YouTube"; ClientSize = new Size(800, 420); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 3 }; root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(root); root.Controls.Add(StudioTheme.Caption("Confira artista, título e duração. Confirmar uma versão prepara a faixa automaticamente.", 10), 0, 0); root.Controls.Add(list, 0, 1); foreach (var value in candidates) list.Items.Add(new Item(value)); if (list.Items.Count > 0) list.SelectedIndex = 0;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 8, 0, 0) }; var okay = new StudioButton { Text = "Usar esta versão", Size = new Size(170, 34), Enabled = candidates.Count > 0 }; var cancel = new StudioButton { Text = "Cancelar", Size = new Size(110, 34), DialogResult = DialogResult.Cancel }; actions.Controls.Add(okay); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 2); CancelButton = cancel; okay.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); }; Shown += (_, _) => StudioTheme.DarkTitle(this); StudioTheme.Apply(this); ResumeLayout(true);
    }
    private sealed record Item(YouTubeCandidate Value) { public override string ToString() => $"{Value.Title}  /  {Value.Artist}  /  {(Value.DurationSeconds is double seconds ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss") : "—")}"; }
}
