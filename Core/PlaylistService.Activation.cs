using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Burnout3.PlaylistStudio.Core;

public sealed partial class PlaylistService
{
    private sealed class ActivationRecord
    {
        public Guid PlaylistId{get;set;}
        public string PlaylistName{get;set;}="";
        public string MusicRoot{get;set;}="";
        public string Soundtrack{get;set;}="";
        public string ConfigPath{get;set;}="";
        public string? InstalledVersion{get;set;}
        public bool AutomaticSupported{get;set;}
        public List<Guid> TrackIds{get;set;}=[];
        public Dictionary<Guid,string> AudioHashes{get;set;}=[];
    }
    public TakedawnCompatibility DetectTakedawn(string?executablePath=null)
    {
        var candidates=executablePath!=null?new[]{executablePath}:new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Takedawn","Takedawn.exe"),Path.Combine(AppContext.BaseDirectory,"Takedawn.exe")};
        foreach(string file in candidates.Where(File.Exists))
        {
            var info=FileVersionInfo.GetVersionInfo(file);var match=Regex.Match(info.ProductVersion??info.FileVersion??"",@"\d+\.\d+(?:\.\d+){0,2}");
            if(match.Success&&Version.TryParse(match.Value,out var version))return new(Path.GetFullPath(file),version,version>=new Version(0,2,1),version>=new Version(0,2,1)?"Takedawn suporta selecionar música customizada ao iniciar a corrida.":"Takedawn "+version+": após abrir o jogo, selecione a soundtrack no EA GAMES TRAX com X/A; início automático não é suportado nesta versão.");
            return new(Path.GetFullPath(file),null,false,"Executável encontrado sem versão confiável. A seleção deverá ser manual no EA GAMES TRAX.");
        }
        return new(null,null,false,"Versão do Takedawn não identificada. A seleção deverá ser manual no EA GAMES TRAX.");
    }
    public async Task<ActivationResult> ActivatePlaylistAsync(Guid playlistId,string?configPath=null,Version?version=null,CancellationToken cancellationToken=default,string?executablePath=null)
    {
        await Worker.WaitAsync(cancellationToken);FileStream?workerLock=null;
        try
        {
            EnsureTakedawnClosed();workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            var playlist=State(d=>Snapshot(Find(d,playlistId)));
            if(playlist.Tracks.Count==0)throw new InvalidOperationException("A playlist está vazia. Adicione e processe as faixas antes de carregar.");
            if(playlist.Tracks.Any(t=>t.Status!=TrackStatus.Downloaded||t.MetadataDirty||t.LocalPath==null))throw new InvalidOperationException("Todas as faixas devem ter MP3 pronto, sem falhas ou metadados pendentes, antes de carregar a playlist exclusiva.");
            var hashes=playlist.Tracks.ToDictionary(t=>t.Id,t=>SafeFiles.Hash(AudioPath(playlistId,t.Id)));
            var reusable=State(d=>d.Active);
            if(reusable!=null&&reusable.PlaylistId==playlistId&&reusable.PlaylistName==playlist.Name&&reusable.TrackIds.SequenceEqual(playlist.Tracks.Select(t=>t.Id))&&reusable.AudioHashes.Count==hashes.Count&&hashes.All(h=>reusable.AudioHashes.TryGetValue(h.Key,out var sha)&&sha==h.Value)&&ActiveFilesReady(reusable))
                return CommitActivation(reusable,configPath,version??DetectTakedawn(executablePath).InstalledVersion);
            string activeParent=Path.Combine(DataDirectory,"active",playlistId.ToString("N"));string container=SafeFiles.Within(DataDirectory,Path.Combine(activeParent,Guid.NewGuid().ToString("N")));
            var installed=await Task.Run(()=>InstallPlaylist(playlistId,container,cancellationToken,false),cancellationToken);
            string ascii=Regex.Replace(playlist.Name.Normalize(NormalizationForm.FormD),@"\p{Mn}","");ascii=Regex.Replace(ascii,@"[^A-Za-z0-9 _-]","").Trim();if(ascii.Length==0)ascii="Playlist";if(ascii.Length>20)ascii=ascii[..20];ascii+="_"+playlistId.ToString("N")[..8];
            string destination=SafeFiles.Within(container,Path.Combine(container,ascii));
            if(!Path.GetFullPath(installed.PlaylistDirectory).Equals(destination,StringComparison.OrdinalIgnoreCase))Directory.Move(installed.PlaylistDirectory,destination);
            var record=new ActivationRecord{PlaylistId=playlistId,PlaylistName=playlist.Name,MusicRoot=destination,Soundtrack=ascii,TrackIds=playlist.Tracks.Select(t=>t.Id).ToList(),AudioHashes=hashes};
            if(!ActiveFilesReady(record))throw new InvalidDataException("A exportação ativa contém áudio inesperado, ausente ou alterado. A configuração anterior foi preservada.");
            var current=State(d=>Snapshot(Find(d,playlistId)));
            if(current.Name!=playlist.Name||!current.Tracks.Select(t=>t.Id).SequenceEqual(record.TrackIds)||current.Tracks.Any(t=>t.Status!=TrackStatus.Downloaded||t.MetadataDirty||t.LocalPath==null||SafeFiles.Hash(t.LocalPath)!=hashes[t.Id]))throw new IOException("A playlist mudou durante a ativação. Carregue novamente; a configuração anterior foi preservada.");
            cancellationToken.ThrowIfCancellationRequested();
            return CommitActivation(record,configPath,version??DetectTakedawn(executablePath).InstalledVersion);
        }
        finally{workerLock?.Dispose();Worker.Release();}
    }
    private ActivationResult CommitActivation(ActivationRecord record,string? configPath,Version? version)
    {
        string target=Path.GetFullPath(configPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Takedawn","settings.toml"));
        byte[]? original=File.Exists(target)?File.ReadAllBytes(target):null;
        ConfigResult? configured=null;string? writtenHash=null;bool automatic=version!=null&&version>=new Version(0,2,1);
        try
        {
            configured=ConfigureTakedawn(target,record.MusicRoot,version,record.Soundtrack,true);writtenHash=SafeFiles.Hash(configured.ConfigPath);
            record.ConfigPath=configured.ConfigPath;record.AutomaticSupported=automatic;record.InstalledVersion=version?.ToString();
            State(d=>
            {
                var playlist=Find(d,record.PlaylistId);
                if(!playlist.Tracks.Select(t=>t.Id).SequenceEqual(record.TrackIds)||playlist.Tracks.Any(t=>t.Status!=TrackStatus.Downloaded||t.MetadataDirty))throw new IOException("Playlist mudou durante a ativação.");
                d.Active=record;var manifest=ReadActiveManifest(record);
                foreach(var track in playlist.Tracks){var audio=manifest.Files.First(f=>f.Value.TrackId==track.Id&&f.Key.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase));track.InstalledPath=Path.Combine(record.MusicRoot,audio.Key);}return 0;
            },true);
        }
        catch
        {
            // Never overwrite an external config change made after this operation's write.
            if(configured!=null&&writtenHash!=null&&File.Exists(target)&&SafeFiles.Hash(target)==writtenHash)
            {
                if(original==null)File.Delete(target);else SafeFiles.Write(target,original,false);
            }
            throw;
        }
        string message=automatic?"Playlist exclusiva carregada. O Takedawn selecionará esta soundtrack ao iniciar a próxima corrida; não é uma troca forçada durante uma sessão em execução.":"Pasta exclusiva configurada. Esta versão não tem ativação automática confirmada: no jogo, abra EA GAMES TRAX e selecione '"+record.Soundtrack+"' com X/A. A seleção ativa em jogo não foi verificada.";
        return new(record.PlaylistId,record.PlaylistName,record.MusicRoot,version,automatic,configured.ConfigPath,configured.BackupPath,message);
    }
    private static InstallManifest ReadActiveManifest(ActivationRecord record)
    {
        string path=SafeFiles.Within(record.MusicRoot,Path.Combine(record.MusicRoot,".playlist-studio.json"));
        var manifest=JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(path),Json)??throw new InvalidDataException("Manifesto ativo inválido.");
        if(manifest.Version!=1||manifest.PlaylistId!=record.PlaylistId)throw new InvalidDataException("Identidade da exportação ativa inválida.");return manifest;
    }
    private static bool ActiveFilesReady(ActivationRecord record)
    {
        try
        {
            if(!Directory.Exists(record.MusicRoot)||Directory.EnumerateDirectories(record.MusicRoot).Any())return false;
            var manifest=ReadActiveManifest(record);var audio=manifest.Files.Where(p=>p.Key.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase)).ToArray();
            if(audio.Length!=record.TrackIds.Count||!audio.Select(a=>a.Value.TrackId).ToHashSet().SetEquals(record.TrackIds))return false;
            var actual=Directory.EnumerateFiles(record.MusicRoot).Where(f=>new[]{".mp3",".flac",".ogg",".wav"}.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
            if(actual.Length!=audio.Length)return false;
            foreach(var item in audio)
            {
                ValidateManagedName(record.MusicRoot,item.Key,item.Value.TrackId);string path=SafeFiles.Within(record.MusicRoot,Path.Combine(record.MusicRoot,item.Key));
                if(!File.Exists(path)||!record.AudioHashes.TryGetValue(item.Value.TrackId,out var expected)||SafeFiles.Hash(path)!=expected||item.Value.Sha256!=expected)return false;
            }
            return true;
        }
        catch(Exception ex)when(ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException){return false;}
    }
    public ActivePlaylistStatus GetActiveStatus(string?configPath=null)
    {
        var active=State(d=>d.Active);if(active==null)return new(null,null,null,false,false,false,true,0,null,"Nenhuma playlist exclusiva foi carregada por este aplicativo.");
        bool files=ActiveFilesReady(active);var current=State(d=>d.Playlists.FirstOrDefault(p=>p.Id==active.PlaylistId));
        files=files&&current!=null&&current.Tracks.Select(t=>t.Id).SequenceEqual(active.TrackIds)&&current.Tracks.All(t=>t.Status==TrackStatus.Downloaded&&!t.MetadataDirty&&File.Exists(AudioPath(active.PlaylistId,t.Id))&&SafeFiles.Hash(AudioPath(active.PlaylistId,t.Id))==active.AudioHashes[t.Id]);
        bool match=false;string path=configPath??active.ConfigPath;
        try
        {
            string text=File.ReadAllText(path);
            if(Regex.Matches(text,@"(?m)^\s*\[audio\]\s*(?:#[^\r\n]*)?$").Count!=1)throw new InvalidDataException("Seção audio ausente ou duplicada.");
            var audio=Regex.Match(text,@"(?ms)^\s*\[audio\]\s*(?:#[^\r\n]*)?[\r\n]+(?<body>.*?)(?=^\s*\[|\z)").Groups["body"].Value;
            string? Value(string key){var matches=Regex.Matches(audio,@"(?m)^\s*"+key+@"\s*=\s*(?<v>[^\r\n]+)$");if(matches.Count!=1)return null;var line=matches[0].Groups["v"].Value.Trim();if(line.StartsWith('"')){try{return JsonSerializer.Deserialize<string>(line);}catch(JsonException){return null;}}if(line.StartsWith('\'')&&line.EndsWith('\''))return line[1..^1];return line;}
            string? dir=Value("custom_music_dir");match=dir!=null&&Path.GetFullPath(dir).Equals(Path.GetFullPath(active.MusicRoot),StringComparison.OrdinalIgnoreCase);
            if(active.AutomaticSupported)match=match&&Value("custom_music_at_start")=="true"&&Value("custom_music_soundtrack")==active.Soundtrack;
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException){match=false;}
        Version.TryParse(active.InstalledVersion,out var version);
        string message=!match?"Configuração não corresponde à playlist carregada; carregue novamente.":!files?"Áudio ativo/biblioteca mudou ou contém faixas extras; carregue novamente para isolar a playlist.":active.AutomaticSupported?"Pasta exclusiva e início automático configurados para a próxima corrida. Reprodução atual não verificada.":"Pasta exclusiva configurada; selecione manualmente a soundtrack no EA GAMES TRAX com X/A. Reprodução atual não verificada.";
        return new(active.PlaylistId,current?.Name??active.PlaylistName,active.MusicRoot,match,files,active.AutomaticSupported,!active.AutomaticSupported,active.TrackIds.Count,version,message);
    }
}
