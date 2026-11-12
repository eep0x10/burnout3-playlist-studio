using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Burnout3.PlaylistStudio.Core;
internal static class SyncTests
{
    internal static async Task<int> RunAsync(string parent,string tools)
    {
        int passed=0;void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
        string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string root=Path.Combine(parent,"sync");var runner=new FakeRunner();using var http=new HttpClient(new FakeHttp());var service=new PlaylistService(Path.Combine(root,"data"),runner,http,tools){GameRunningForTests=()=>false};string library=Path.Combine(service.DataDirectory,"library.json");
        string Listing(params string[]ids)=>JsonSerializer.Serialize(new{title="Remote fixture",playlist_count=ids.Length,entries=ids.Select(id=>new{id,title="Remote "+id,artist="Artist",url="https://www.youtube.com/watch?v="+id,duration=12})});
        Playlist Current(Guid id)=>service.GetLibrary().Playlists.Single(p=>p.Id==id);
        var p=service.CreatePlaylist("Remote sync");runner.RemotePlaylistJson=Listing("a","b","a");var imported=await service.ImportYouTubePlaylistAsync(p.Id,"https://youtube.com/playlist?list=PLsynthetic");
        Check(imported.Complete&&Current(p.Id).SourceKind==PlaylistSourceKind.YouTube&&Current(p.Id).Tracks.Select(t=>t.RemoteId).SequenceEqual(new[]{"youtube:a","youtube:b","youtube:a"}),"Remote import atomically persists canonical source and duplicate identities");
        await service.SyncPlaylistAsync(p.Id,false);var initial=Current(p.Id).Tracks;Guid first=initial[0].Id,removed=initial[1].Id,secondDuplicate=initial[2].Id;string removedCache=initial[1].LocalPath!;string removedLyrics=initial[1].LyricsPath!;
        service.UpdateTrack(p.Id,first,"Custom title","Custom artist");await service.SyncPlaylistAsync(p.Id,false);string customYouTube=Current(p.Id).Tracks[0].YouTubeUrl!;
        var manual=service.AddTrack(p.Id,"Manual independent track");await service.SyncPlaylistAsync(p.Id,false);
        string music=Path.Combine(root,"Music");var installation=await service.InstallPlaylistAsync(p.Id,music);string removedExport=Directory.GetFiles(installation.PlaylistDirectory,"*.mp3").Single(f=>f.Contains(removed.ToString("N")[..8]));string stock=Path.Combine(music,"unmanaged.mp3");File.WriteAllText(stock,"do not delete");
        runner.RemotePlaylistJson=Listing("a","a","c");var changed=await service.SyncPlaylistAsync(p.Id);var tracks=Current(p.Id).Tracks;
        Check(changed.Added==1&&changed.Removed==1&&changed.Complete&&tracks.Select(t=>t.Id).Take(2).SequenceEqual(new[]{first,secondDuplicate}),"Complete reconcile preserves duplicate occurrence GUIDs and remote order");
        Check(tracks.Any(t=>t.Id==manual.Id&&t.RemoteId==null)&&tracks[0].Title=="Custom title"&&tracks[0].YouTubeUrl==customYouTube,"Manual records, explicit metadata override and selected match survive sync");
        Check(tracks.First(t=>t.RemoteId=="youtube:c").Status==TrackStatus.Downloaded,"New remote track is automatically processed");
        Check(!File.Exists(removedCache)&&!File.Exists(removedLyrics)&&!File.Exists(removedExport)&&File.Exists(stock),"Removed track deletes owned cache, lyrics and export while unmanaged audio survives");
        int downloadsBefore=runner.Calls.Count(c=>c.Args.Contains("--format"));await service.SyncPlaylistAsync(p.Id);Check(runner.Calls.Count(c=>c.Args.Contains("--format"))==downloadsBefore,"Identical refresh reuses downloaded audio without downloading again");
        runner.RemotePlaylistJson=JsonSerializer.Serialize(new{playlist_count=3,entries=new[]{new{id="c",title="Renamed remote c",artist="New artist",url="https://youtube.com/watch?v=c"}}});
        var partial=await service.SyncPlaylistAsync(p.Id);Check(!partial.Complete&&partial.Removed==0&&Current(p.Id).Tracks.Count==4,"Incomplete remote count cannot purge missing remote occurrences");
        Check(Current(p.Id).Tracks.First(t=>t.RemoteId=="youtube:c").Title=="Renamed remote c","Remote metadata updates when there is no explicit local override");
        string before=Hash(library);runner.RemotePlaylistJson="NOT JSON";bool bad=false;try{await service.SyncPlaylistAsync(p.Id);}catch(JsonException){bad=true;}Check(bad&&Hash(library)==before,"Failed remote enumeration leaves byte-identical library before reconciliation");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool cancelled=false;try{await service.SyncPlaylistAsync(p.Id,cancellationToken:cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled&&Hash(library)==before,"Cancelled fetch leaves previous catalog intact");}
        runner.RemotePlaylistJson=Listing();await service.SyncPlaylistAsync(p.Id);Check(Current(p.Id).Tracks.Count==1&&Current(p.Id).Tracks[0].Id==manual.Id,"Proven complete empty source removes remote tracks while retaining manual track");
        var legacy=service.CreatePlaylist("Legacy bind");var legacyTrack=service.AddTrack(legacy.Id,"https://youtu.be/legacy");service.BindPlaylistSource(legacy.Id,"https://youtube.com/playlist?list=PLlegacy");runner.RemotePlaylistJson=Listing("legacy");await service.SyncPlaylistAsync(legacy.Id);Check(Current(legacy.Id).Tracks.Count==1&&Current(legacy.Id).Tracks[0].Id==legacyTrack.Id&&Current(legacy.Id).Tracks[0].RemoteId=="youtube:legacy","Explicit initial binding adopts exact existing YouTube identity without duplicate cache");
        service.BindPlaylistSource(legacy.Id,null);Check(Current(legacy.Id).SourceKind==PlaylistSourceKind.None&&Current(legacy.Id).Tracks[0].RemoteId==null,"Unbinding preserves records as manual without invented source");
        var active=service.CreatePlaylist("Active sync");runner.RemotePlaylistJson=Listing("x","y");await service.ImportYouTubePlaylistAsync(active.Id,"https://youtube.com/playlist?list=PLactive");await service.SyncPlaylistAsync(active.Id,false);string config=Path.Combine(root,"config","settings.toml");Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,"[audio]\nvolume = 0.5\n[video]\nwidth = 1920\n");var activated=await service.ActivatePlaylistAsync(active.Id,config,new Version(0,3,1));string oldAudio=Directory.GetFiles(activated.MusicRoot,"*.mp3")[0];string oldAudioHash=Hash(oldAudio),configHash=Hash(config);
        service.GameRunningForTests=()=>true;runner.RemotePlaylistJson=Listing("y","z");var deferred=await service.SyncPlaylistAsync(active.Id);Check(deferred.PendingActivation&&!deferred.AppliedToGame&&Hash(config)==configHash&&File.Exists(oldAudio)&&Hash(oldAudio)==oldAudioHash,"Game-open sync prepares catalog/audio but preserves playing files and exact config");
        var restarted=new PlaylistService(service.DataDirectory,runner,http,tools){GameRunningForTests=()=>false};Check(restarted.HasPendingChanges&&restarted.GetLibrary().Playlists.Single(t=>t.Id==active.Id).PendingActivation,"Pending activation survives service restart");
        int priorRemoteQueries=runner.Calls.Count(c=>c.Args.Contains("--flat-playlist"));var retried=await restarted.RetryPendingChangesAsync();Check(retried.Any(r=>r.AppliedToGame)&&!restarted.GetLibrary().Playlists.Single(t=>t.Id==active.Id).PendingActivation&&runner.Calls.Count(c=>c.Args.Contains("--flat-playlist"))==priorRemoteQueries,"Closed-game pending retry applies once without remote network refresh");
        Check(!Directory.GetFiles(activated.MusicRoot,"*.mp3").Any(),"Obsolete active snapshot audio is cleaned with ownership hashes after switch");
        restarted.DeletePlaylist(active.Id);Check(!restarted.GetLibrary().Playlists.Any(t=>t.Id==active.Id)&&restarted.GetLibrary().ActivePlaylistId==null&&!File.ReadAllText(config).Contains("custom_music_")&&File.ReadAllText(config).Contains("volume = 0.5")&&File.ReadAllText(config).Contains("width = 1920")&&File.Exists(config+".bak"),"Deleting active playlist clears only owned music config with backup and preserves unrelated TOML");
        service.GameRunningForTests=()=>false;string originalLocal=Path.Combine(root,"original.wav");File.WriteAllText(originalLocal,"original source");var local=service.CreatePlaylist("Local delete");service.ImportLocalFiles(local.Id,new[]{originalLocal});await service.SyncPlaylistAsync(local.Id,false);string localCache=Current(local.Id).Tracks[0].LocalPath!;service.DeletePlaylist(local.Id);Check(File.Exists(originalLocal)&&!File.Exists(localCache),"Deleting local playlist removes generated cache but never original imported audio");
        var modified=service.CreatePlaylist("Modified cached file");service.ImportLocalFiles(modified.Id,new[]{originalLocal});await service.SyncPlaylistAsync(modified.Id,false);var modifiedTrack=Current(modified.Id).Tracks[0];File.AppendAllText(modifiedTrack.LocalPath!," user modification");service.RemoveTrack(modified.Id,modifiedTrack.Id);Check(File.Exists(modifiedTrack.LocalPath!)&&Current(modified.Id).SyncWarning!.Contains("modificado"),"User-modified managed cache is preserved with concrete cleanup warning");
        var spotifyHandler=new SyncSpotifyHttp();using var spotifyHttp=new HttpClient(spotifyHandler);var spotifyService=new PlaylistService(Path.Combine(root,"spotify"),new FakeRunner(),spotifyHttp,tools){GameRunningForTests=()=>false};var sp=spotifyService.CreatePlaylist("Spotify source");await spotifyService.ImportSpotifyAsync(sp.Id,"https://open.spotify.com/playlist/fixture","EPHEMERAL");var originalSpotify=spotifyService.GetLibrary().Playlists.Single().Tracks.Select(t=>t.Id).ToArray();
        spotifyHandler.EmbedOneOnly=true;var spotifyPartial=await spotifyService.SyncPlaylistAsync(sp.Id,spotifyAccessToken:null);Check(!spotifyPartial.Complete&&spotifyPartial.Removed==0&&spotifyService.GetLibrary().Playlists.Single().Tracks.Count>=2,"Spotify public embed cannot purge records missing from visible subset");
        spotifyHandler.EmbedOneOnly=false;spotifyHandler.SnapshotChanged=true;var movingSnapshot=await spotifyService.SyncPlaylistAsync(sp.Id,spotifyAccessToken:"EPHEMERAL");Check(!movingSnapshot.Complete&&movingSnapshot.Removed==0,"Spotify snapshot change during pagination prevents authoritative removals");
        spotifyHandler.SnapshotChanged=false;spotifyHandler.FailSecondPage=true;string spotifyLibrary=Path.Combine(spotifyService.DataDirectory,"library.json"),spotifyBefore=Hash(spotifyLibrary);bool pageFailed=false;try{await spotifyService.SyncPlaylistAsync(sp.Id,spotifyAccessToken:"EPHEMERAL");}catch(HttpRequestException){pageFailed=true;}Check(pageFailed&&Hash(spotifyLibrary)==spotifyBefore,"Spotify second-page failure preserves byte-identical prior catalog");
        spotifyHandler.FailSecondPage=false;spotifyHandler.Empty=true;var emptySpotify=await spotifyService.SyncPlaylistAsync(sp.Id,spotifyAccessToken:"EPHEMERAL");Check(emptySpotify.Complete&&spotifyService.GetLibrary().Playlists.Single().Tracks.Count==0,"Stable authorized Spotify snapshot with total0 can reconcile empty source");
        Check(!File.ReadAllText(spotifyLibrary).Contains("EPHEMERAL"),"Sync access token remains ephemeral and absent from persisted library");
        string deletionFixture=Path.Combine(root,"handle-delete.mp3");File.WriteAllText(deletionFixture,"owned");string deletionHash=Hash(deletionFixture);bool renameBlocked=false;
        bool deletedByHandle=VerifiedFileDeletion.Delete(deletionFixture,deletionHash,()=>{try{File.Move(deletionFixture,deletionFixture+".moved");File.WriteAllText(deletionFixture,"UNMANAGED SWAP");}catch(IOException){renameBlocked=true;}return false;});
        Check(deletedByHandle&&renameBlocked&&!File.Exists(deletionFixture)&&!File.Exists(deletionFixture+".moved"),"Verified deletion locks rename and deletes the hashed handle, never a swapped path");
        File.WriteAllText(deletionFixture,"modified");Check(!VerifiedFileDeletion.Delete(deletionFixture,deletionHash,()=>false)&&File.ReadAllText(deletionFixture)=="modified","Handle deletion refuses modified bytes without marking them for deletion");
        var sourceA=service.CreatePlaylist("Live source A");service.ImportLocalFiles(sourceA.Id,new[]{originalLocal});await service.SyncPlaylistAsync(sourceA.Id,false);var sourceATrack=Current(sourceA.Id).Tracks[0];
        var sourceB=service.CreatePlaylist("Live source B");var sourceBTrack=service.ImportLocalFiles(sourceB.Id,new[]{sourceATrack.LocalPath!})[0];service.RemoveTrack(sourceA.Id,sourceATrack.Id);
        Check(File.Exists(sourceATrack.LocalPath!)&&Current(sourceB.Id).Tracks[0].Status==TrackStatus.Added,"Removing managed cache preserves it while another unprepared track uses it as original source");
        Check(!service.HasPendingChanges,"Live-source deferred cleanup does not keep retry timer perpetually busy");
        service.RemoveTrack(sourceB.Id,sourceBTrack.Id);Check(!File.Exists(sourceATrack.LocalPath!)&&File.Exists(originalLocal),"Deferred owned orphan is cleaned when its last live source reference is removed");
        var unsafePlaylist=service.CreatePlaylist("Containment test");service.ImportLocalFiles(unsafePlaylist.Id,new[]{originalLocal});await service.SyncPlaylistAsync(unsafePlaylist.Id,false);var unsafeTrack=Current(unsafePlaylist.Id).Tracks[0];var unsafeInstall=await service.InstallPlaylistAsync(unsafePlaylist.Id,Path.Combine(root,"UnsafeMusic"));
        string unsafeManifest=Path.Combine(unsafeInstall.PlaylistDirectory,".playlist-studio.json"),unsafeOriginal=File.ReadAllText(unsafeManifest),outsideSentinel=Path.Combine(root,"outside-keep.mp3");File.WriteAllText(outsideSentinel,"UNMANAGED");
        using(var manifestDoc=JsonDocument.Parse(unsafeOriginal))
        {
            var values=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(unsafeOriginal)!;var badFiles=new Dictionary<string,object>{{"../outside-keep.mp3",new{TrackId=unsafeTrack.Id,Sha256=Hash(outsideSentinel)}}};
            values["Files"]=JsonSerializer.SerializeToElement(badFiles);File.WriteAllText(unsafeManifest,JsonSerializer.Serialize(values));
        }
        bool traversalRejected=false;try{service.RemoveTrack(unsafePlaylist.Id,unsafeTrack.Id);}catch(InvalidDataException){traversalRejected=true;}
        Check(traversalRejected&&File.ReadAllText(outsideSentinel)=="UNMANAGED","Malicious cleanup manifest traversal is rejected before touching unrelated file");
        File.WriteAllText(unsafeManifest,unsafeOriginal);await service.RetryPendingChangesAsync();
        return passed;
    }
}
internal sealed class SyncSpotifyHttp:HttpMessageHandler
{
    public bool EmbedOneOnly,SnapshotChanged,FailSecondPage,Empty;private int metadataRequests;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        string url=request.RequestUri!.ToString(),body;
        if(url.Contains("lrclib"))body="{\"instrumental\":true}";
        else if(url.Contains("/embed/"))body="<script id=\"__NEXT_DATA__\">{\"props\":{\"pageProps\":{\"state\":{\"data\":{\"entity\":{\"name\":\"Fixture\",\"trackList\":[{\"uri\":\"spotify:track:a\",\"title\":\"A\",\"subtitle\":\"Artist\",\"duration\":12000}]}}}}}}</script>";
        else if(url.Contains("/items"))
        {
            bool second=url.Contains("offset=1");if(second&&FailSecondPage)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            body=JsonSerializer.Serialize(new{total=Empty?0:2,next=Empty||second?null:"https://api.spotify.com/v1/playlists/fixture/items?offset=1",items=Empty?Array.Empty<object>():new object[]{new{item=new{uri="spotify:track:"+(second?"b":"a"),name=second?"B":"A",type="track",artists=new[]{new{name="Artist"}}}}}});
        }
        else {metadataRequests++;body=JsonSerializer.Serialize(new{name="Fixture",snapshot_id=SnapshotChanged?"snapshot"+metadataRequests:"stable"});}
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}

