using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Burnout3.PlaylistStudio.Core;
public sealed partial class PlaylistService
{
    private sealed record DeferredFile(string Root,string Path,string Hash);
    private static HashSet<string> LiveSources(LibraryData d)=>d.Playlists.SelectMany(p=>p.Tracks).Select(t=>t.Source).Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
    private sealed record RemovalRecord(Guid PlaylistId,Track Track);
    private sealed record ExportRecord(Guid PlaylistId,string Root,string Folder);
    // Test seam controls only process availability, never touches a game/config itself.
    internal Func<bool>? GameRunningForTests {get;set;}
    private bool GameRunning()=>GameRunningForTests?.Invoke()??IsTakedawnRunning();
    private static bool IsTakedawnRunning(){var ps=System.Diagnostics.Process.GetProcessesByName("Takedawn");try{return ps.Length>0;}finally{foreach(var p in ps)p.Dispose();}}
    public async Task<SyncResult> SyncPlaylistAsync(Guid playlistId,bool refreshRemote=true,string?configPath=null,string?executablePath=null,IProgress<OperationProgress>?progress=null,CancellationToken cancellationToken=default,string?spotifyAccessToken=null)
    {
        // Serialize refreshes across processes, without nesting the existing Process/Activate Worker lock.
        string syncPath=SafeFiles.Within(DataDirectory,Path.Combine(DataDirectory,".sync.lock"));
        using var syncLock=new FileStream(syncPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var original=State(d=>Snapshot(Find(d,playlistId)));RemoteListing?listing=null;var warnings=new List<string>();int added=0,removed=0;
        if(refreshRemote&&original.SourceUrl!=null)
        {progress?.Report(new(playlistId,Guid.Empty,"sync",null,"Obtendo lista remota antes de alterar a biblioteca…"));listing=await FetchRemote(original.SourceUrl,spotifyAccessToken,cancellationToken);warnings.AddRange(listing.Warnings);}
        cancellationToken.ThrowIfCancellationRequested();
        await Worker.WaitAsync(cancellationToken);
        try
        {
            using var workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(listing!=null)
            State(d=>
            {
                var p=Find(d,playlistId);if(p.SourceUrl!=original.SourceUrl)throw new IOException("Origem mudou durante a atualização; tente novamente.");
                if(p.AdoptExistingOnNextSync&&p.SourceKind==PlaylistSourceKind.YouTube)
                {
                    var candidates=p.Tracks.Where(t=>t.RemoteId==null&&p.SourceAdoptionCandidates.Contains(t.Id)).ToList();
                    foreach(var item in listing.Tracks)
                    {
                        string id=item.Id["youtube:".Length..];
                        var match=candidates.FirstOrDefault(t=>IsSameYouTubeVideo(t.YouTubeUrl??t.Source,id));
                        if(match!=null){match.RemoteId=item.Id;candidates.Remove(match);p.SourceAdoptionCandidates.Remove(match.Id);}
                    }
                }
                if(listing.Complete){p.AdoptExistingOnNextSync=false;p.SourceAdoptionCandidates.Clear();}
                var byId=p.Tracks.Where(t=>t.RemoteId!=null).GroupBy(t=>t.RemoteId!).ToDictionary(g=>g.Key,g=>new Queue<Track>(g));
                var ordered=new List<Track>();var retained=new HashSet<Guid>();
                foreach(var item in listing.Tracks)
                {
                    if(byId.TryGetValue(item.Id,out var queue)&&queue.Count>0){var existing=queue.Dequeue();if(!existing.MetadataOverride&&(existing.Title!=item.Title||existing.Artist!=item.Artist||existing.Album!=item.Album)){existing.Title=item.Title;existing.Artist=item.Artist;existing.Album=item.Album;existing.MetadataDirty=true;}ordered.Add(existing);retained.Add(existing.Id);}
                    else {var track=NewRemote(item);ordered.Add(track);retained.Add(track.Id);added++;}
                }
                var manual=p.Tracks.Where(t=>t.RemoteId==null).ToList();
                var absent=p.Tracks.Where(t=>t.RemoteId!=null&&!retained.Contains(t.Id)).ToList();
                if(!listing.Complete)ordered.AddRange(absent);
                else foreach(var track in absent){d.Removals.Add(new(playlistId,track));d.Jobs.RemoveAll(j=>j.TrackId==track.Id);removed++;}
                ordered.AddRange(manual);if(ordered.Count>500)throw new InvalidOperationException("Atualização ultrapassa500 faixas com itens preservados; biblioteca intacta.");
                p.Tracks=ordered;p.LastSyncUtc=DateTime.UtcNow;p.LastSyncComplete=listing.Complete;p.SyncWarning=string.Join(" ",warnings);
                if(d.Active?.PlaylistId==playlistId)p.PendingActivation=true;return 0;
            },true);
        }
        finally{Worker.Release();}
        cancellationToken.ThrowIfCancellationRequested();
        var current=State(d=>Snapshot(Find(d,playlistId)));OperationResult processed=new(0,0,0,[]);
        if(current.Tracks.Count>0)
        {
            processed=await ProcessPlaylistAsync(playlistId,progress,cancellationToken);warnings.AddRange(processed.Warnings);
            if(processed.Cancelled>0)cancellationToken.ThrowIfCancellationRequested();
        }
        bool applied=false,pending=false;var active=State(d=>d.Active);
        if(active?.PlaylistId==playlistId)
        {
            pending=true;State(d=>{Find(d,playlistId).PendingActivation=true;return 0;},true);
            if(!GameRunning())
            {
                current=State(d=>Snapshot(Find(d,playlistId)));
                if(current.Tracks.Count==0){await ClearEmptyActiveAsync(playlistId,cancellationToken);applied=true;pending=false;}
                else if(current.Tracks.All(t=>t.Status==TrackStatus.Downloaded&&!t.MetadataDirty&&t.LocalPath!=null))
                {
                    try{await ActivatePlaylistAsync(playlistId,configPath??active.ConfigPath,Version.TryParse(active.InstalledVersion,out var version)?version:null,cancellationToken,executablePath);applied=true;pending=false;}
                    catch(InvalidOperationException)when(GameRunning()){warnings.Add("Takedawn abriu durante a aplicação; nova tentativa pendente.");}
                }
            }
            else warnings.Add("Takedawn aberto: biblioteca preparada; áudio ativo/configuração preservados até fechar o jogo e atualizar novamente.");
        }
        await CleanupPendingAsync(warnings,cancellationToken);
        State(d=>{var p=Find(d,playlistId);p.PendingActivation=pending;p.SyncWarning=string.Join(" ",warnings);return 0;},true);
        return new(added,removed,listing?.Complete??current.LastSyncComplete??true,processed.Succeeded,processed.Failed,applied,pending,warnings);
    }
    private static bool IsSameYouTubeVideo(string url,string id)
    {
        if(!IsYouTubeUrl(url))return false;var uri=new Uri(url);
        if(uri.Host.Equals("youtu.be",StringComparison.OrdinalIgnoreCase))return uri.AbsolutePath.Trim('/')==id;
        var match=Regex.Match(uri.Query,@"(?:[?&])v=([^&]+)");return match.Success&&Uri.UnescapeDataString(match.Groups[1].Value)==id;
    }
    private void RecordManagedFiles(Guid playlistId,Guid trackId)
    {
        var hashes=new Dictionary<string,string>();
        foreach(string ext in new[]{"mp3","lrc","txt"})foreach(string suffix in new[]{"",".bak"})
        {string path=SidecarPath(playlistId,trackId,ext)+suffix;if(File.Exists(path))hashes[Path.GetFileName(path)]=SafeFiles.Hash(path);}
        State(d=>{var t=Find(d,playlistId,trackId);foreach(var pair in hashes)t.ManagedHashes[pair.Key]=pair.Value;return 0;},true);
    }
    private void RemoveCatalog(Guid playlistId,Guid?trackId)
    {
        Worker.Wait();
        try
        {
            using var workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            State(d=>
            {
                var p=Find(d,playlistId);var removed=trackId==null?p.Tracks.ToArray():new[]{Find(d,playlistId,trackId.Value)};
                foreach(var track in removed){d.Removals.Add(new(playlistId,track));d.Jobs.RemoveAll(j=>j.TrackId==track.Id);p.Tracks.Remove(track);}
                if(d.Active?.PlaylistId==playlistId)p.PendingActivation=true;
                if(trackId==null){d.Playlists.Remove(p);d.DeletedPlaylists.Add(playlistId);}return 0;
            },true);
        }
        finally{Worker.Release();}
        // Catalog removal remains available during gameplay; files/config remain untouched until retry.
        if(!GameRunning())
        {
            var warnings=new List<string>();CleanupPendingAsync(warnings,CancellationToken.None).GetAwaiter().GetResult();
            if(warnings.Count>0)State(d=>{var p=d.Playlists.FirstOrDefault(p=>p.Id==playlistId);if(p!=null)p.SyncWarning=string.Join(" ",warnings);return 0;},true);
        }
    }
    public bool HasPendingChanges=>State(d=>d.Removals.Count>0||d.DeletedPlaylists.Count>0||d.Playlists.Any(p=>p.PendingActivation)||d.DeferredFiles.Any(f=>!LiveSources(d).Contains(f.Path)));
    public async Task<IReadOnlyList<SyncResult>> RetryPendingChangesAsync(string?configPath=null,string?executablePath=null,IProgress<OperationProgress>?progress=null,CancellationToken cancellationToken=default)
    {
        if(GameRunning())return [];
        var results=new List<SyncResult>();foreach(var id in State(d=>d.Playlists.Where(p=>p.PendingActivation).Select(p=>p.Id).ToArray()))results.Add(await SyncPlaylistAsync(id,false,configPath,executablePath,progress,cancellationToken));
        var cleanupWarnings=new List<string>();await CleanupPendingAsync(cleanupWarnings,cancellationToken);if(cleanupWarnings.Count>0)results.Add(new(0,0,true,0,0,false,HasPendingChanges,cleanupWarnings));return results;
    }
    private async Task ClearEmptyActiveAsync(Guid id,CancellationToken ct)
    {
        await Worker.WaitAsync(ct);
        try
        {
            using var workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(GameRunning())throw new InvalidOperationException("Takedawn abriu durante a desativação; aplicação pendente.");var active=State(d=>d.Active);if(active?.PlaylistId!=id)return;
            string path=active.ConfigPath;byte[]?before=File.Exists(path)?File.ReadAllBytes(path):null;string?written=null;
            if(before!=null)
            {
                // Only remove keys while their values still identify our exact active export.
                var status=GetActiveStatus(path);if(!status.ConfigMatches)throw new IOException("Configuração alterada externamente; desativação automática suspensa.");
                string text=Encoding.UTF8.GetString(before);string newline=text.Contains("\r\n")?"\r\n":"\n";var lines=text.Split(["\r\n","\n"],StringSplitOptions.None).ToList();bool audio=false;
                lines=lines.Where(line=>{if(Regex.IsMatch(line,@"^\s*\["))audio=Regex.IsMatch(line,@"^\s*\[audio\]\s*(#.*)?$");return !audio||!Regex.IsMatch(line,active.AutomaticSupported?@"^\s*(custom_music_dir|custom_music_at_start|custom_music_soundtrack)\s*=":@"^\s*custom_music_dir\s*=");}).ToList();
                EnsureTakedawnClosed();SafeFiles.Write(path,Encoding.UTF8.GetBytes(string.Join(newline,lines)));written=SafeFiles.Hash(path);
            }
            try{State(d=>{if(d.Active?.PlaylistId==id)d.Active=null;var p=d.Playlists.FirstOrDefault(p=>p.Id==id);if(p!=null)p.PendingActivation=false;return 0;},true);}
            catch{if(before!=null&&written!=null&&File.Exists(path)&&SafeFiles.Hash(path)==written)SafeFiles.Write(path,before,false);throw;}
        }
        finally{Worker.Release();}
    }
    private async Task CleanupPendingAsync(List<string>warnings,CancellationToken ct)
    {
        if(GameRunning())return;
        var emptyActive=State(d=>d.Active!=null&&(d.DeletedPlaylists.Contains(d.Active.PlaylistId)||d.Playlists.FirstOrDefault(p=>p.Id==d.Active.PlaylistId)?.Tracks.Count==0)?d.Active.PlaylistId:(Guid?)null);
        if(emptyActive!=null)await ClearEmptyActiveAsync(emptyActive.Value,ct);
        await Worker.WaitAsync(ct);
        try
        {
            using var workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if(GameRunning())return;
            foreach(var deferred in State(d=>d.DeferredFiles.ToArray()))
            {
                SafeFiles.Within(deferred.Root,deferred.Path);
                if(DeleteIfOwned(deferred.Path,deferred.Hash,warnings,deferred.Root))State(d=>{d.DeferredFiles.RemoveAll(f=>f.Path.Equals(deferred.Path,StringComparison.OrdinalIgnoreCase));return 0;},true);
            }
            var snapshot=State(d=>(Removals:d.Removals.ToArray(),Exports:d.Exports.ToArray(),Active:d.Active,Deleted:d.DeletedPlaylists.ToHashSet()));
            foreach(var removal in snapshot.Removals)
            {
                ct.ThrowIfCancellationRequested();
                foreach(var pair in removal.Track.ManagedHashes)
                {
                    string allowedPrefix=removal.Track.Id.ToString("N")+".";
                    if(pair.Key!=Path.GetFileName(pair.Key)||!pair.Key.StartsWith(allowedPrefix,StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(pair.Key,@"^[0-9a-f]{32}\.(mp3|lrc|txt)(\.bak)?$",RegexOptions.IgnoreCase))throw new InvalidDataException("Cache gerenciado inválido; limpeza suspensa.");
                    string path=SafeFiles.Within(DataDirectory,Path.Combine(DataDirectory,"audio",removal.PlaylistId.ToString("N"),pair.Key));DeleteIfOwned(path,pair.Value,warnings);
                }
                if(removal.Track.ManagedHashes.Count==0&&File.Exists(AudioPath(removal.PlaylistId,removal.Track.Id)))warnings.Add(removal.Track.Title+": cache legado sem hash de propriedade preservado.");
            }
            var exports=snapshot.Exports.ToList();
            // Discover only our bounded active snapshot tree, including exports created before this version.
            string activeRoot=Path.Combine(DataDirectory,"active");
            if(Directory.Exists(activeRoot))
                foreach(string playlistDir in Directory.EnumerateDirectories(activeRoot))
                {
                    SafeFiles.Within(DataDirectory,Path.Combine(playlistDir,".guard"));
                    foreach(string generation in Directory.EnumerateDirectories(playlistDir))
                    {SafeFiles.Within(DataDirectory,Path.Combine(generation,".guard"));foreach(string folder in Directory.EnumerateDirectories(generation))if(File.Exists(SafeFiles.Within(DataDirectory,Path.Combine(folder,".playlist-studio.json"))))exports.Add(new(Guid.Empty,DataDirectory,folder));}
                }
            foreach(var export in exports.DistinctBy(e=>e.Folder,StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();string folder=SafeFiles.Within(export.Root,Path.Combine(export.Folder,".guard"));folder=Path.GetDirectoryName(folder)!;
                bool isCurrent=snapshot.Active!=null&&folder.Equals(snapshot.Active.MusicRoot,StringComparison.OrdinalIgnoreCase);if(isCurrent)continue;
                string mf=SafeFiles.Within(export.Root,Path.Combine(folder,".playlist-studio.json"));if(!File.Exists(mf))continue;
                var manifest=JsonSerializer.Deserialize<InstallManifest>(File.ReadAllBytes(mf),Json)??throw new InvalidDataException("Manifesto inválido.");
                if(manifest.Version!=1||(export.PlaylistId!=Guid.Empty&&manifest.PlaylistId!=export.PlaylistId))throw new InvalidDataException("Identidade do manifesto inválida.");
                bool obsolete=folder.StartsWith(Path.GetFullPath(activeRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||snapshot.Deleted.Contains(manifest.PlaylistId);
                var removeIds=snapshot.Removals.Where(r=>r.PlaylistId==manifest.PlaylistId).Select(r=>r.Track.Id).ToHashSet();
                foreach(var item in manifest.Files)ValidateManagedName(folder,item.Key,item.Value.TrackId);
                foreach(var item in manifest.Files.Where(f=>obsolete||removeIds.Contains(f.Value.TrackId)).ToArray())
                {DeleteIfOwned(SafeFiles.Within(export.Root,Path.Combine(folder,item.Key)),item.Value.Sha256,warnings,export.Root);manifest.Files.Remove(item.Key);}
                SafeFiles.Write(mf,JsonSerializer.SerializeToUtf8Bytes(manifest,Json));
            }
            State(d=>{foreach(var r in snapshot.Removals)d.Removals.RemoveAll(x=>x.PlaylistId==r.PlaylistId&&x.Track.Id==r.Track.Id);foreach(var id in snapshot.Deleted)if(d.Active?.PlaylistId!=id)d.DeletedPlaylists.Remove(id);return 0;},true);
        }
        finally{Worker.Release();}
    }
    private bool DeleteIfOwned(string path,string hash,List<string>warnings,string?boundary=null)
    {
        // Keep library mutex through the live-source check and verified-handle deletion.
        return State(d=>
        {
            if(!File.Exists(path))return true;
            if(LiveSources(d).Contains(path))
            {
                warnings.Add(Path.GetFileName(path)+": fonte local de outra faixa viva; preservado até remover a última referência.");
                if(!d.DeferredFiles.Any(f=>f.Path.Equals(path,StringComparison.OrdinalIgnoreCase)))d.DeferredFiles.Add(new(boundary??DataDirectory,path,hash));return false;
            }
            if(SafeFiles.Hash(path)!=hash){warnings.Add(Path.GetFileName(path)+": modificado; preservado.");return true;}
            if(!VerifiedFileDeletion.Delete(path,hash,GameRunning))warnings.Add(Path.GetFileName(path)+": hash mudou; preservado.");return true;
        },true);
    }
}
