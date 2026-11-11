using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Burnout3.PlaylistStudio.Core;

internal static class SafeFiles
{
    public static string Within(string root,string path)
    {
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        path=Path.GetFullPath(path);
        if(!path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Caminho fora da pasta gerenciada.");
        for(string? part=Path.GetDirectoryName(path);part!=null&&part.Length>=root.Length;part=Path.GetDirectoryName(part))
            if(Directory.Exists(part)&&(File.GetAttributes(part)&FileAttributes.ReparsePoint)!=0)throw new IOException("Links/junções não são aceitos dentro da pasta gerenciada.");
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Arquivo gerenciado não pode ser um link.");
        return path;
    }
    public static string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream));}
    public static void Write(string path,byte[] bytes,bool backup=true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(bytes);f.Flush(true);}if(File.Exists(path))File.Replace(temp,path,backup?path+".bak":null);else File.Move(temp,path);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static string Name(string text,int max=70)
    {
        var invalid=Path.GetInvalidFileNameChars();var clean=new string(text.Normalize().Where(c=>!char.IsControl(c)&&!invalid.Contains(c)).ToArray()).Trim().Trim('.');
        if(string.IsNullOrEmpty(clean)||clean is "." or "..")clean="Playlist";
        if(clean.Length>max)clean=clean[..max];return clean;
    }
}
public sealed class ProcessToolRunner : IToolRunner
{
    public async Task<ToolResult> RunAsync(string executable,IReadOnlyList<string> arguments,Action<string>? output=null,CancellationToken cancellationToken=default)
    {
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in arguments)start.ArgumentList.Add(arg);
        using var process=new Process{StartInfo=start};if(!process.Start())throw new IOException("Não foi possível iniciar a ferramenta.");
        using var cancel=cancellationToken.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}});
        var stdout=new StringBuilder();var stderr=new StringBuilder();
        async Task Drain(StreamReader reader,StringBuilder target)
        {while(await reader.ReadLineAsync(cancellationToken) is { } line){if(target.Length<8_000_000)target.AppendLine(line);output?.Invoke(line);}}
        try{await Task.WhenAll(Drain(process.StandardOutput,stdout),Drain(process.StandardError,stderr),process.WaitForExitAsync(cancellationToken));}
        finally
        {
            if(!process.HasExited){try{process.Kill(true);}catch(InvalidOperationException){}}
            try{await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){}
        }
        cancellationToken.ThrowIfCancellationRequested();return new(process.ExitCode,stdout.ToString(),stderr.ToString());
    }
}
