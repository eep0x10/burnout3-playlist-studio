namespace Burnout3.PlaylistStudio.App;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var index = Array.IndexOf(args, "--data");
        var config = Array.IndexOf(args, "--config");
        try { Application.Run(new StudioForm(index >= 0 && index + 1 < args.Length ? args[index + 1] : null, config >= 0 && config + 1 < args.Length ? args[config + 1] : null)); }
        catch (Exception ex) { MessageBox.Show("Não foi possível abrir a biblioteca portátil. Extraia o aplicativo em uma pasta em que você possa escrever.\n\n" + ex.Message, "Burnout 3 Playlist Studio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
