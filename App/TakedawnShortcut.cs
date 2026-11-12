using System.Runtime.InteropServices;

namespace Burnout3.PlaylistStudio.App;

public static class TakedawnShortcut
{
    public static string Create(string gameExecutable, string applicationDirectory, string shortcutPath, bool overwrite = false)
    {
        string game = Path.GetFullPath(gameExecutable);
        string folder = Path.GetFullPath(applicationDirectory);
        string output = Path.GetFullPath(shortcutPath);
        if (!Path.GetFileName(game).Equals("Takedawn.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(game))
            throw new ArgumentException("Selecione o Takedawn.exe existente do jogo, não um instalador ou outro programa.");
        string script = Path.Combine(folder, "Launch-Takedawn.vbs");
        if (!File.Exists(script) || !File.Exists(Path.Combine(folder, "BurnoutPlaylistStudio.exe")))
            throw new FileNotFoundException("Mantenha Launch-Takedawn.vbs e BurnoutPlaylistStudio.exe juntos na pasta extraída do aplicativo.");
        if (game.Contains('%') || folder.Contains('%') || output.Contains('%'))
            throw new ArgumentException("Use pastas sem o caractere % para este atalho e para os dois aplicativos.");
        if (!Path.GetExtension(output).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("O destino do atalho deve terminar em .lnk.");
        if (!Directory.Exists(Path.GetDirectoryName(output)))
            throw new DirectoryNotFoundException("A pasta de destino do atalho não foi encontrada.");
        if (File.Exists(output) && !overwrite)
            throw new IOException("Já existe um atalho com este nome. Confirme a substituição antes de continuar.");
        string host = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wscript.exe");
        if (!File.Exists(host)) throw new FileNotFoundException("O Windows Script Host não foi encontrado neste Windows.");
        object? shell = null, shortcut = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("O criador de atalhos do Windows não está disponível.");
            shell = Activator.CreateInstance(type) ?? throw new InvalidOperationException("Não foi possível acessar os atalhos do Windows.");
            shortcut = ((dynamic)shell).CreateShortcut(output);
            dynamic link = shortcut;
            link.TargetPath = host;
            link.Arguments = "//nologo \"" + script + "\" \"" + game + "\"";
            link.WorkingDirectory = Path.GetDirectoryName(game)!;
            link.IconLocation = game + ",0";
            link.Description = "Takedawn com Playlist Studio: abre o gerenciador minimizado e fecha junto com o jogo.";
            link.Save();
            return output;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
