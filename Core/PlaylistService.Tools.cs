using System.IO.Compression;

using System.Security.Cryptography;

using System.Text.Json;

using System.Text.RegularExpressions;



namespace Burnout3.PlaylistStudio.Core;



public sealed partial class PlaylistService

{

    private sealed record ToolReceipt(string Source,string Sha256,string Version);

    private static readonly SemaphoreSlim SetupGate=new(1,1);

    private readonly Dictionary<string,(long Length,DateTime Modified,string Hash)> toolHashes=new();

    private string ToolManifest=>Path.Combine(toolsDirectory,"tools-manifest.json");

    private Dictionary<string,ToolReceipt> Receipts()

    {try{return File.Exists(ToolManifest)?JsonSerializer.Deserialize<Dictionary<string,ToolReceipt>>(File.ReadAllBytes(ToolManifest),Json)??[]:[];}catch(JsonException){throw new InvalidDataException("Manifesto de ferramentas inválido; preserve a pasta tools e prepare novamente.");}}

    private bool ToolValid(string name,Dictionary<string,ToolReceipt> receipts)

    {

        string path=SafeFiles.Within(toolsDirectory,Path.Combine(toolsDirectory,name));if(!File.Exists(path))return false;if(!receipts.TryGetValue(name,out var receipt))return false;

        var info=new FileInfo(path);lock(toolHashes)

        {if(!toolHashes.TryGetValue(path,out var cached)||cached.Length!=info.Length||cached.Modified!=info.LastWriteTimeUtc){cached=(info.Length,info.LastWriteTimeUtc,SafeFiles.Hash(path));toolHashes[path]=cached;}return cached.Hash.Equals(receipt.Sha256,StringComparison.OrdinalIgnoreCase);}

    }

    public ToolStatus GetTools()

    {

        var receipts=Receipts();var missing=new[]{"yt-dlp.exe","ffmpeg.exe","ffprobe.exe","deno.exe"}.Where(n=>!ToolValid(n,receipts)).ToArray();

        return new(missing.Length==0,Path.Combine(toolsDirectory,"yt-dlp.exe"),Path.Combine(toolsDirectory,"ffmpeg.exe"),Path.Combine(toolsDirectory,"deno.exe"),missing);

    }

    public async Task<ToolStatus> EnsureToolsAsync(IProgress<OperationProgress>?progress=null,CancellationToken cancellationToken=default)

    {

        await SetupGate.WaitAsync(cancellationToken);string stage=Path.Combine(toolsDirectory,".setup-"+Guid.NewGuid().ToString("N"));

        try

        {

            Directory.CreateDirectory(toolsDirectory);Directory.CreateDirectory(stage);

            using var installLock=new FileStream(Path.Combine(toolsDirectory,".setup.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);

            var receipts=Receipts();

            async Task Download(string url,string target,string expected)

            {

                progress?.Report(new(Guid.Empty,Guid.Empty,"tools",null,"Baixando e verificando "+Path.GetFileName(target)));

                using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("Burnout3PlaylistStudio/1.0");using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellationToken);response.EnsureSuccessStatusCode();

                if(response.Content.Headers.ContentLength>300_000_000)throw new IOException("Ferramenta excede limite de download.");

                await using(var input=await response.Content.ReadAsStreamAsync(cancellationToken))await using(var output=File.Create(target))

                {var buffer=new byte[65536];long total=0;int n;while((n=await input.ReadAsync(buffer,cancellationToken))>0){total+=n;if(total>300_000_000)throw new IOException("Ferramenta excede limite de download.");await output.WriteAsync(buffer.AsMemory(0,n),cancellationToken);}}

                if(!SafeFiles.Hash(target).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("SHA-256 da ferramenta não confere. Nada foi executado.");

            }

            void Place(string staged,string name,string source,string version)

            {

                string dest=SafeFiles.Within(toolsDirectory,Path.Combine(toolsDirectory,name));string sha=SafeFiles.Hash(staged);

                if(File.Exists(dest))File.Replace(staged,dest,dest+".bak");else File.Move(staged,dest);

                receipts[name]=new(source,sha,version);SafeFiles.Write(ToolManifest,JsonSerializer.SerializeToUtf8Bytes(receipts,Json));

            }

            if(!ToolValid("yt-dlp.exe",receipts))

            {

                const string url="https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.26/yt-dlp.exe";string file=Path.Combine(stage,"yt-dlp.exe");

                await Download(url,file,"f5389d3698ecde4ef06f4e6b1ce0294fe9ac1396228c8990b8d3416d6d177a7d");Place(file,"yt-dlp.exe",url,"2026.10.26");

            }

            if(!ToolValid("ffmpeg.exe",receipts)||!ToolValid("ffprobe.exe",receipts))

            {

                const string url="https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.2-essentials_build.zip";

                using var check=await http.GetAsync(url+".sha256",cancellationToken);check.EnsureSuccessStatusCode();string checksum=await check.Content.ReadAsStringAsync(cancellationToken);var hash=Regex.Match(checksum,"\\b[a-fA-F0-9]{64}\\b");if(!hash.Success)throw new InvalidDataException("Checksum oficial FFmpeg indisponível.");

                string zip=Path.Combine(stage,"ffmpeg.zip");await Download(url,zip,hash.Value);using var archive=ZipFile.OpenRead(zip);

                foreach(string exe in new[]{"ffmpeg.exe","ffprobe.exe"})

                {var entry=archive.Entries.SingleOrDefault(e=>e.Name==exe)??throw new InvalidDataException("Arquivo FFmpeg esperado não encontrado.");if(entry.Length>250_000_000)throw new InvalidDataException("Executável FFmpeg excessivo.");string extracted=Path.Combine(stage,exe);entry.ExtractToFile(extracted);Place(extracted,exe,url,"9.0.2");}

            }

            if(!ToolValid("deno.exe",receipts))

            {

                const string url="https://github.com/denoland/deno/releases/download/v2.9.8/deno-x86_64-pc-windows-msvc.zip";string zip=Path.Combine(stage,"deno.zip");

                await Download(url,zip,"cba84b26e0df5ae7f74fc66b86a95b866872a86292ebd84fa1e959927edd93bf");using var archive=ZipFile.OpenRead(zip);var entry=archive.Entries.Single(e=>e.Name=="deno.exe");string extracted=Path.Combine(stage,"deno.exe");if(entry.Length>250_000_000)throw new InvalidDataException("Executável Deno excessivo.");entry.ExtractToFile(extracted);Place(extracted,"deno.exe",url,"2.9.8");

            }

            progress?.Report(new(Guid.Empty,Guid.Empty,"tools",100,"Ferramentas prontas; hashes e fontes registrados em tools-manifest.json."));return GetTools();

        }

        finally

        {

            // This unique task-owned directory contains only downloaded temporary setup files.

            if(Directory.Exists(stage))Directory.Delete(stage,true);SetupGate.Release();

        }

    }

}
