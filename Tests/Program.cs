using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Burnout3.PlaylistStudio.Core;

if(args.Length==2&&args[0]=="--pss-holdfile")
{
    using(var fixtureHandle=new FileStream(args[1],FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();}
    Console.WriteLine("CLOSED");Console.Out.Flush();Console.ReadLine();return;
}
var root=Path.Combine(AppContext.BaseDirectory,"data",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var data=Path.Combine(root,"data");var tools=Path.Combine(root,"tools");Directory.CreateDirectory(tools);
var runner=new FakeRunner();var handler=new FakeHttp();using var http=new HttpClient(handler);var service=new PlaylistService(data,runner,http,tools){GameRunningForTests=()=>false};int passed=0;
void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
void Reject(Action action,string message){bool failed=false;try{action();}catch(Exception ex)when(ex is ArgumentException or IOException or InvalidDataException or InvalidOperationException){failed=true;}Check(failed,message);}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
// Download/conversion tools use an injected runner. The PSS test starts only this same test executable.
foreach(string name in new[]{"yt-dlp.exe","ffmpeg.exe","ffprobe.exe","deno.exe"})File.WriteAllText(Path.Combine(tools,name),"SYNTHETIC TEST STUB");
Check(!service.GetTools().Ready,"Pre-existing executables without receipts are not trusted");
var receipts=new Dictionary<string,object>();foreach(string name in new[]{"yt-dlp.exe","ffmpeg.exe","ffprobe.exe","deno.exe"})receipts[name]=new{Source="https://example.invalid/synthetic-test-only",Sha256=Hash(Path.Combine(tools,name)),Version="synthetic"};
File.WriteAllText(Path.Combine(tools,"tools-manifest.json"),JsonSerializer.Serialize(receipts));Check(service.GetTools().Ready,"Synthetic test receipts validate exact local hashes");
var p=service.CreatePlaylist("Roadtrip");Check(service.RenamePlaylist(p.Id,"Night Drive").Name=="Night Drive","Playlist create/rename persist");
var first=service.AddTrack(p.Id,"Synthetic Song","Unit Artist");var second=service.AddTrack(p.Id,"Other Song","Other Artist");service.ReorderTrack(p.Id,second.Id,0);
Check(service.GetLibrary().Playlists.Single().Tracks[0].Id==second.Id,"Track reordering persists by ID");
service.UpdateTrack(p.Id,first.Id,"Synthetic Song Updated","Unit Artist","Unit Album");Check(service.GetLibrary().Playlists[0].Tracks.First(t=>t.Id==first.Id).Album=="Unit Album","Track metadata update persists");
var snapshot=service.GetLibrary();snapshot.Playlists[0].Tracks.Clear();Check(service.GetLibrary().Playlists[0].Tracks.Count==2,"Snapshots cannot mutate persisted library");
service.RemoveTrack(p.Id,second.Id);Check(service.GetLibrary().Playlists[0].Tracks.Count==1,"Track removal changes catalog only");
Check(File.Exists(Path.Combine(data,"library.json.bak")),"Atomic library writes retain prior JSON backup");
var other=new PlaylistService(data,runner,http,tools);await Task.WhenAll(Task.Run(()=>{for(int i=0;i<5;i++)service.CreatePlaylist("A"+i);}),Task.Run(()=>{for(int i=0;i<5;i++)other.CreatePlaylist("B"+i);}));
Check(service.GetLibrary().Playlists.Count==11,"Separate service instances serialize catalog writes without lost updates");
Reject(()=>service.AddTrack(p.Id,"https://evil.example/not-youtube"),"Non-YouTube download URLs rejected");
Reject(()=>service.AddTrack(p.Id,"https://www.youtube.com/watch?v=unit&list=PLunit"),"Single-track flow rejects implicit playlist import");
Reject(()=>service.UpdateTrack(p.Id,first.Id,"Valid",new string('a',251)),"Oversized metadata rejected");
var csv=service.ImportCsv(p.Id,"Track Name,Artist Name(s),Album Name\n\"Song, Part 1\",\"Band, The\",Album\n");Check(csv.Added==1&&csv.Complete&&service.GetLibrary().Playlists[0].Tracks.Last().Artist=="Band, The","Quoted CSV metadata import complete");
var text=service.ImportText(p.Id,"Artist - One\nArtist - Two\n");Check(text.Added==2&&text.Complete,"Text import adds artist/title rows");
Reject(()=>service.ImportText(p.Id,new string('A',1_000_001)),"Text import is bounded");
var spotify=await service.ImportSpotifyAsync(p.Id,"https://open.spotify.com/playlist/UNITTEST");Check(spotify.Added==2&&!spotify.Complete&&spotify.Message.Contains("Integralidade",StringComparison.OrdinalIgnoreCase),"Public Spotify embed imports visible metadata with unknown completeness");
var oauthPlaylist=service.CreatePlaylist("OAuth import");var oauth=await service.ImportSpotifyAsync(oauthPlaylist.Id,"https://open.spotify.com/playlist/OAUTH","TEST_TOKEN_MEMORY_ONLY");Check(oauth.Added==1&&!oauth.Complete&&oauth.Warnings.Any(w=>w.Contains("Paginação")),"Repeated official Spotify pagination cannot claim completeness");
Check(!File.ReadAllText(Path.Combine(data,"library.json")).Contains("TEST_TOKEN_MEMORY_ONLY"),"Spotify bearer token is never persisted");
var found=await service.SearchYouTubeAsync("Unit Artist Synthetic Song");Check(found[0].Title=="Synthetic Song Official Audio","YouTube search ranks a matching official candidate over a cover");
var query=service.AddTrack(p.Id,"--exec cmd /c echo SHOULD_NOT_EXECUTE");var selected=await service.ResolveTrackAsync(p.Id,query.Id);Check(selected.YouTubeUrl!.StartsWith("https://www.youtube.com/watch"),"Text query resolves to canonical YouTube metadata");
service.UpdateTrack(p.Id,query.Id,selected.Title,selected.Artist,"User Album");
var processed=await service.ProcessTrackAsync(p.Id,query.Id);var downloaded=service.GetLibrary().Playlists[0].Tracks.First(t=>t.Id==query.Id);
Check(processed.Succeeded==1&&downloaded.LocalPath!=null&&Path.IsPathFullyQualified(downloaded.LocalPath)&&File.Exists(downloaded.LocalPath),"Processing yields an owned MP3 with absolute snapshot path");
Check(runner.Calls.Any(c=>c.Exe.EndsWith("ffmpeg.exe")&&c.Args.Contains("-b:a")&&c.Args.Contains("192k")&&!c.Args.Contains("-q:a")),"New MP3 encoding uses 192k CBR for stable native player/game timing");
Check(runner.Calls.Any(c=>c.Exe.EndsWith("ffmpeg.exe")&&c.Args.Contains("album=User Album")),"User album metadata survives conversion instead of being replaced by playlist name");
Check(downloaded.LyricsStatus==LyricsStatus.Synced&&File.Exists(downloaded.LyricsPath),"Automatic synthetic LRCLIB result writes a synchronized sidecar");
Check(runner.Calls.Where(c=>c.Exe.EndsWith("yt-dlp.exe")).All(c=>c.Args.Contains("--ignore-config")&&c.Args.Contains("--"))&&runner.Calls.Any(c=>c.Args.Any(a=>a.StartsWith("ytsearch5:")&&a.Contains("--exec"))),"yt-dlp always ignores global config and user query stays one safe argument");
Check(runner.Calls.All(c=>c.Exe.EndsWith("yt-dlp.exe")||c.Exe.EndsWith("ffmpeg.exe")||c.Exe.EndsWith("ffprobe.exe")),"No shell, cmd or PowerShell is invoked by downloader");
handler.FailLyrics=true;var noLyrics=service.AddTrack(p.Id,"No lyrics test");var noLyricsRun=await service.ProcessTrackAsync(p.Id,noLyrics.Id);var noLyricsState=service.GetLibrary().Playlists[0].Tracks.First(t=>t.Id==noLyrics.Id);
Check(noLyricsRun.Succeeded==1&&noLyricsState.Status==TrackStatus.Downloaded&&noLyricsState.LyricsStatus==LyricsStatus.Failed,"Lyrics failure is best effort and does not fail audio");handler.FailLyrics=false;
var localPlaylist=service.CreatePlaylist("Local ../../ Night");string source=Path.Combine(root,"synthetic.wav");File.WriteAllBytes(source,Encoding.ASCII.GetBytes("SYNTHETIC TEST AUDIO"));string sourceHash=Hash(source);
var locals=service.ImportLocalFiles(localPlaylist.Id,[source,source]);await service.ProcessPlaylistAsync(localPlaylist.Id);Check(service.GetLibrary().Playlists.First(x=>x.Id==localPlaylist.Id).Tracks.All(t=>t.DurationSeconds==12),"Local duration is obtained from ffprobe and persisted");Check(Hash(source)==sourceHash,"Import/conversion never modifies original local audio");
string music=Path.Combine(root,"Music","Takedawn");Directory.CreateDirectory(Path.Combine(music,"Stock"));string stock=Path.Combine(music,"Stock","untouched.mp3");File.WriteAllText(stock,"UNMANAGED STOCK");string stockHash=Hash(stock);
var install=await service.InstallPlaylistAsync(localPlaylist.Id,music);var audioFiles=Directory.GetFiles(install.PlaylistDirectory,"*.mp3");Check(install.Copied==2&&audioFiles.Length==2&&audioFiles.All(f=>Path.GetFileName(f).StartsWith("000")),"Installer copies MP3s into one soundtrack with natural-order filenames");
Check(Hash(stock)==stockHash&&File.Exists(Path.Combine(install.PlaylistDirectory,".playlist-studio.json")),"Stock music preserved; installation manifest created");
string ownedA=service.GetLibrary().Playlists.First(pl=>pl.Id==localPlaylist.Id).Tracks.First(t=>t.Id==locals[0].Id).InstalledPath!;string ownedB=service.GetLibrary().Playlists.First(pl=>pl.Id==localPlaylist.Id).Tracks.First(t=>t.Id==locals[1].Id).InstalledPath!;string oldA=Hash(ownedA);
service.UpdateTrack(localPlaylist.Id,locals[0].Id,"Download not ready","Unit Artist");runner.FailConvert=true;await service.ProcessTrackAsync(localPlaylist.Id,locals[0].Id);runner.FailConvert=false;service.RemoveTrack(localPlaylist.Id,locals[1].Id);
await service.InstallPlaylistAsync(localPlaylist.Id,music);Check(File.Exists(ownedA)&&Hash(ownedA)==oldA&&!File.Exists(ownedB),"Unavailable current track retains installed audio; removed track ID deletes only its unchanged managed audio");
Check(Directory.Exists(Path.Combine(Path.GetDirectoryName(music)!,"PlaylistStudio-Backups"))&&!Directory.Exists(Path.Combine(music,"PlaylistStudio-Backups")),"Managed backups stay outside scanned music root");
// User modifications detach ownership and are not overwritten/deleted.
service.UpdateTrack(p.Id,query.Id,"New tags","Unit Artist");await service.ProcessTrackAsync(p.Id,query.Id);var install2=await service.InstallPlaylistAsync(p.Id,music);string userModified=service.GetLibrary().Playlists[0].Tracks.First(t=>t.Id==query.Id).InstalledPath!;File.WriteAllText(userModified,"USER MODIFICATION");string modifiedHash=Hash(userModified);await service.InstallPlaylistAsync(p.Id,music);Check(Hash(userModified)==modifiedHash,"User-modified installed file is never overwritten");
string unmanaged=Path.Combine(install2.PlaylistDirectory,"keep.txt");File.WriteAllText(unmanaged,"USER NOTE");service.RemoveTrack(p.Id,query.Id);await service.InstallPlaylistAsync(p.Id,music);Check(File.Exists(userModified)&&File.ReadAllText(unmanaged)=="USER NOTE"&&Hash(stock)==stockHash,"Removing a track preserves modified/unmanaged/stock files");
string config=Path.Combine(root,"sandbox-config","settings.toml");Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,"# retain this\r\n[video]\r\nwidth = 1280\r\n[audio]\r\nvolume = 0.8\r\ncustom_music_dir = 'old'\r\n[other]\r\nvalue = true\r\n");byte[] configBefore=File.ReadAllBytes(config);
var configured=service.ConfigureTakedawn(config,music);string configAfter=File.ReadAllText(config);Check(configured.BackupPath!=null&&File.ReadAllBytes(configured.BackupPath).SequenceEqual(configBefore),"Takedawn config backup preserves exact original bytes");
Check(configAfter.Contains("width = 1280")&&configAfter.Contains("volume = 0.8")&&configAfter.Contains("value = true")&&configAfter.Contains("# retain this")&&configAfter.Contains("custom_music_dir = \"")&&!configAfter.Contains("custom_music_at_start")&&!configAfter.Contains("custom_music_soundtrack"),"Only compatible v0.2.0 audio.custom_music_dir is patched; other TOML sections preserved");
foreach(string nonCanonical in new[]{"[\"audio\"]\ncustom_music_dir = 'old'\n","audio = { custom_music_dir = 'old' }\n","[audio]\n'custom_music_dir' = 'old'\n"})
{File.WriteAllText(config,nonCanonical);string nonHash=Hash(config);Reject(()=>service.ConfigureTakedawn(config,music),"Noncanonical TOML is rejected without unsafe rewrite");Check(Hash(config)==nonHash,"Rejected TOML remains byte-identical");}
string emojiRoot=Path.Combine(root,"Music🎵");Directory.CreateDirectory(emojiRoot);File.WriteAllText(config,"[audio]\nvolume = 0.5\n");service.ConfigureTakedawn(config,emojiRoot);Check(File.ReadAllText(config).Contains("🎵")&&!File.ReadAllText(config).Contains("\\uD83D"),"TOML path preserves Unicode scalars rather than invalid surrogate escapes");
string evilManifest=Path.Combine(install.PlaylistDirectory,".playlist-studio.json");string priorManifest=File.ReadAllText(evilManifest);File.WriteAllText(evilManifest,JsonSerializer.Serialize(new{Version=1,PlaylistId=localPlaylist.Id,PlaylistName="Test",Files=new Dictionary<string,object>{{"../../Stock/untouched.mp3",new{TrackId=locals[0].Id,Sha256=stockHash}}}}));bool traversal=false;try{await service.InstallPlaylistAsync(localPlaylist.Id,music);}catch(InvalidDataException){traversal=true;}Check(traversal&&Hash(stock)==stockHash,"Malicious managed manifest cannot traverse paths or delete stock");File.WriteAllText(evilManifest,priorManifest);
string untrusted=Path.Combine(root,"untrusted-tools");Directory.CreateDirectory(untrusted);var badToolsService=new PlaylistService(Path.Combine(root,"bad-data"),runner,new HttpClient(new BadToolsHttp()),untrusted);bool badHash=false;try{await badToolsService.EnsureToolsAsync();}catch(InvalidDataException){badHash=true;}Check(badHash&&!File.Exists(Path.Combine(untrusted,"yt-dlp.exe")),"Bootstrap rejects wrong SHA before installing/executing tools");
var cancelTrack=service.AddTrack(p.Id,"Cancel test");runner.WaitForCancel=true;using(var cts=new CancellationTokenSource(150)){var cancelled=await service.ProcessTrackAsync(p.Id,cancelTrack.Id,cancellationToken:cts.Token);Check(cancelled.Cancelled==1,"Cancellation propagates to the active tool and finite queue");}runner.WaitForCancel=false;
Check(!Directory.EnumerateFiles(Path.Combine(data,"staging"),"*",SearchOption.AllDirectories).Any(),"Cancelled/failed processing cleans only owned staging files");
Check(service.GetLibrary().Jobs.All(j=>j.Status is not JobStatus.Running and not JobStatus.Queued),"Completed operations leave terminal persisted job states");
var empty=service.CreatePlaylist("Delete catalog");service.DeletePlaylist(empty.Id);Check(!service.GetLibrary().Playlists.Any(pl=>pl.Id==empty.Id),"Playlist delete removes catalog entry");
// Activation exports a strict, flat selected-playlist snapshot; never the whole music library.
var activePlaylist=service.CreatePlaylist("Áctive 🎵 Playlist");service.ImportLocalFiles(activePlaylist.Id,[source,source]);await service.ProcessPlaylistAsync(activePlaylist.Id);
string activeConfig=Path.Combine(root,"active-config","settings.toml");Directory.CreateDirectory(Path.GetDirectoryName(activeConfig)!);File.WriteAllText(activeConfig,"[video]\nwidth = 1280\n[audio]\nvolume = 0.7\n");
string priorMusicRoot=service.GetLibrary().TakedawnRoot;
var activatedOld=await service.ActivatePlaylistAsync(activePlaylist.Id,activeConfig,new Version(0,2,0));
var oldConfigText=File.ReadAllText(activeConfig);var activeOld=service.GetActiveStatus(activeConfig);
Check(!activatedOld.AutomaticSupported&&activatedOld.Message.Contains("X/A")&&!oldConfigText.Contains("custom_music_at_start")&&!oldConfigText.Contains("custom_music_soundtrack"),"Takedawn 0.2.0 activation configures only exclusive directory with explicit manual selection");
Check(activeOld.ConfigMatches&&activeOld.FilesReady&&activeOld.ManualSelectionRequired&&activeOld.TrackCount==2,"Active status validates old-version config and exact selected audio files");
Check(Directory.GetFiles(activatedOld.MusicRoot,"*.mp3").Length==2&&!Directory.EnumerateDirectories(activatedOld.MusicRoot).Any()&&service.GetLibrary().TakedawnRoot==priorMusicRoot,"Exclusive export is flat and preserves ordinary music-root preference");
var activatedNew=await service.ActivatePlaylistAsync(activePlaylist.Id,activeConfig,new Version(0,3,1));string newCfg=File.ReadAllText(activeConfig);var activeNew=service.GetActiveStatus(activeConfig);
Check(activatedNew.MusicRoot==activatedOld.MusicRoot,"Identical active playlist snapshot is reused without copying audio again");
Check(activatedNew.AutomaticSupported&&newCfg.Contains("custom_music_at_start = true")&&newCfg.Contains("custom_music_soundtrack = \"")&&!Regex.IsMatch(newCfg,@"custom_music_soundtrack\s*=\s*\d"),"Compatible version sets automatic start and textual soundtrack name, never an index");
Check(Path.GetFileName(activatedNew.MusicRoot).Length<=31&&Path.GetFileName(activatedNew.MusicRoot).All(c=>c>=32&&c<=126),"Active soundtrack name is ASCII and at most31 display characters");
Check(activeNew.ConfigMatches&&activeNew.FilesReady&&!activeNew.ManualSelectionRequired&&activeNew.InstalledVersion==new Version(0,3,1),"Active status retains explicit verified custom-version compatibility");
byte[] configBeforeFailure=File.ReadAllBytes(activeConfig);bool failedActivation=false;
using(var heldLibrary=new FileStream(Path.Combine(data,"library.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
{try{await service.ActivatePlaylistAsync(activePlaylist.Id,activeConfig,new Version(0,2,0));}catch(IOException){failedActivation=true;}}
Check(failedActivation&&File.ReadAllBytes(activeConfig).SequenceEqual(configBeforeFailure)&&service.GetActiveStatus(activeConfig).AutomaticSupported,"Library commit failure rolls config back to previous exact bytes without claiming new activation");
string extra=Path.Combine(activatedNew.MusicRoot,"unmanaged-extra.mp3");File.WriteAllText(extra,"UNMANAGED AUDIO");Check(!service.GetActiveStatus(activeConfig).FilesReady,"Dropped extra audio invalidates exclusive-playlist status");
var refreshed=await service.ActivatePlaylistAsync(activePlaylist.Id,activeConfig,new Version(0,3,1));Check(refreshed.MusicRoot!=activatedNew.MusicRoot&&File.Exists(extra)&&service.GetActiveStatus(activeConfig).FilesReady,"Contaminated snapshot is isolated anew without deleting user audio");
byte[] validActiveConfig=File.ReadAllBytes(activeConfig);File.AppendAllText(activeConfig,"\n[audio]\ncustom_music_dir = 'other'\n");Check(!service.GetActiveStatus(activeConfig).ConfigMatches,"Duplicated/changed audio section invalidates active config status");
File.WriteAllBytes(activeConfig,validActiveConfig);await service.ActivatePlaylistAsync(activePlaylist.Id,activeConfig,new Version(0,3,1));service.ReorderTrack(activePlaylist.Id,service.GetLibrary().Playlists.First(x=>x.Id==activePlaylist.Id).Tracks[1].Id,0);Check(!service.GetActiveStatus(activeConfig).FilesReady,"Playlist reorder marks current active export stale");
var emptyActive=service.CreatePlaylist("Empty active");bool emptyRefused=false;try{await service.ActivatePlaylistAsync(emptyActive.Id,activeConfig,new Version(0,3,1));}catch(InvalidOperationException){emptyRefused=true;}Check(emptyRefused,"Empty playlist cannot be activated");
service.AddTrack(emptyActive.Id,"Pending track");string untouchedConfigHash=Hash(activeConfig);bool pendingRefused=false;try{await service.ActivatePlaylistAsync(emptyActive.Id,activeConfig,new Version(0,3,1));}catch(InvalidOperationException){pendingRefused=true;}Check(pendingRefused&&Hash(activeConfig)==untouchedConfigHash,"Unready playlist refuses activation before changing configuration");
var compatibility=service.DetectTakedawn(Path.Combine(root,"unknown-takedawn.exe"));Check(compatibility.InstalledVersion==null&&!compatibility.AutomaticSupported,"Unknown executable version never claims automatic support");
string pssFixture=Path.Combine(root,"pss-whitelisted.mp3"),pssUnlisted=Path.Combine(root,"pss-unlisted.mp3");File.WriteAllText(pssFixture,"synthetic");File.WriteAllText(pssUnlisted,"synthetic");
using(var held=new FileStream(pssFixture,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
using(var pssOther=new FileStream(pssUnlisted,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
{var pssFound=PssAudioHandles.Read(Environment.ProcessId,new[]{pssFixture});Check(pssFound.Count==1&&pssFound[0]==pssFixture,"PSS own-PID duplicate query resolves only exact whitelisted synthetic file");}
Check(PssAudioHandles.Read(Environment.ProcessId,new[]{pssFixture}).Count==0,"PSS own-PID no longer observes synthetic file after close");var childInfo=new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true};childInfo.ArgumentList.Add("--pss-holdfile");childInfo.ArgumentList.Add(pssFixture);
using(var syntheticChild=System.Diagnostics.Process.Start(childInfo)!)
{
    try
    {
        Check(await syntheticChild.StandardOutput.ReadLineAsync()=="READY","Synthetic child holds its test fixture without impersonating game");
        Check(PssAudioHandles.ReadSyntheticChild(syntheticChild.Id,new[]{pssFixture}).SequenceEqual(new[]{pssFixture}),"Cross-PID PSS duplicate path query succeeds with only 0x440 process rights");
        syntheticChild.StandardInput.WriteLine();syntheticChild.StandardInput.Flush();Check(await syntheticChild.StandardOutput.ReadLineAsync()=="CLOSED","Synthetic child confirms fixture closed");
        Check(PssAudioHandles.ReadSyntheticChild(syntheticChild.Id,new[]{pssFixture}).Count==0,"Cross-PID observer stops reporting child file after close");
        bool otherPidRefused=false;try{PssAudioHandles.Read(syntheticChild.Id,new[]{pssFixture});}catch(InvalidOperationException){otherPidRefused=true;}Check(otherPidRefused,"Production reader refuses synthetic non-Takedawn process");
    }
    finally{syntheticChild.StandardInput.WriteLine();syntheticChild.StandardInput.Flush();if(!syntheticChild.WaitForExit(3000))syntheticChild.Kill(true);}
}var detectorTest=new StableTrackDetector();var observedTime=DateTime.UtcNow;var observedFiles=new[]{(123,pssFixture)};
Check(detectorTest.Observe(observedFiles,observedTime).Count==0&&detectorTest.Observe(observedFiles,observedTime.AddMilliseconds(500)).Count==0,"Transient scanner handles do not emit");
Check(detectorTest.Observe(observedFiles,observedTime.AddSeconds(1)).Count==1&&detectorTest.Observe(observedFiles,observedTime.AddSeconds(2)).Count==0,"Stable decoder candidate emits once");
detectorTest.Observe([],observedTime.AddSeconds(3));detectorTest.Observe(observedFiles,observedTime.AddSeconds(4));Check(detectorTest.Observe(observedFiles,observedTime.AddSeconds(5)).Count==1,"Closed and reopened same file creates a fresh event");
var ambiguous=new StableTrackDetector();var manyFiles=new[]{(123,pssFixture),(123,pssUnlisted)};ambiguous.Observe(manyFiles,observedTime);Check(ambiguous.Observe(manyFiles,observedTime.AddSeconds(1)).Count==0,"Ambiguous simultaneous open songs do not guess playback order");// Only synthetic logs: never start or inspect the game during tests.
string logFixture=Path.Combine(root,"now-playing.log");string songPath=Path.Combine(root,"managed-song.mp3");
File.WriteAllText(logFixture,$"[SOUNDTRACK] song 00000000: {songPath} (44100 Hz, 2 ch)\n");
var logTail=new NewSongLogTail();Check(logTail.ReadNew(logFixture).Count==0,"Log observer attaches at EOF without stale notifications");
File.AppendAllText(logFixture,$"[SOUNDTRACK] song 00000001: {songPath} (44100 Hz, 2 ch)\n");Check(logTail.ReadNew(logFixture).SequenceEqual(new[]{songPath}),"Exact new decoder-open event resolves source path");
Check(logTail.ReadNew(logFixture).Count==0,"Unchanged log does not duplicate notifications");
File.AppendAllText(logFixture,$"[SOUNDTRACK] song 00000001: {songPath} (44100 Hz, 2 ch)\n");Check(logTail.ReadNew(logFixture).Count==1,"Reopening same song remains a distinct event");
File.AppendAllText(logFixture,"[SOUNDTRACK] cannot play song 00000000\n[SOUNDTRACK] song XXXXXXXX: fake (44100 Hz, 2 ch)\n");Check(logTail.ReadNew(logFixture).Count==0,"Failed decoder and malformed events never notify");
File.WriteAllText(logFixture,"rotated\n");Check(logTail.ReadNew(logFixture).Count==0,"Log truncation discards old events");
File.AppendAllText(logFixture,$"[SOUNDTRACK] song 00000002: {songPath} (48000 Hz, 2 ch)");Check(logTail.ReadNew(logFixture).Count==0,"Partial event waits for complete line");File.AppendAllText(logFixture,"\n");Check(logTail.ReadNew(logFixture).Count==1,"Completed partial line emits once");
passed+=await SyncTests.RunAsync(root,tools);
Console.WriteLine($"SUCCESS {passed} synthetic checks. No real music, game configuration, credentials or network were used. Fixtures: {root}");

sealed class FakeRunner:IToolRunner
{
    public List<(string Exe,List<string> Args)> Calls{get;}=[];public bool FailConvert,WaitForCancel;public string? RemotePlaylistJson;
    public async Task<ToolResult> RunAsync(string executable,IReadOnlyList<string> arguments,Action<string>? output=null,CancellationToken cancellationToken=default)
    {
        var args=arguments.ToList();Calls.Add((executable,args));if(WaitForCancel){await Task.Delay(Timeout.Infinite,cancellationToken);}
        if(executable.EndsWith("ffprobe.exe"))return new(0,"12.0","");
        if(executable.EndsWith("ffmpeg.exe")){if(FailConvert)return new(1,"","synthetic conversion failure");Directory.CreateDirectory(Path.GetDirectoryName(args[^1])!);File.WriteAllBytes(args[^1],Encoding.ASCII.GetBytes("SYNTHETIC MP3: "+string.Join("|",args.Where(a=>a.StartsWith("title=")))));return new(0,"","");}
        if(args.Contains("--dump-single-json")&&args.Contains("--flat-playlist")&&!args.Any(a=>a.StartsWith("ytsearch"))&&RemotePlaylistJson!=null)return new(0,RemotePlaylistJson,"");
        if(args.Contains("--dump-single-json"))
        {if(args.Any(a=>a.StartsWith("ytsearch5:")))return new(0,"{\"entries\":[{\"id\":\"cover\",\"title\":\"Synthetic Song Cover\",\"uploader\":\"Unit Artist\",\"duration\":1},{\"id\":\"unit\",\"title\":\"Synthetic Song Official Audio\",\"artist\":\"Unit Artist\",\"duration\":1}]}","");
            return new(0,"{\"id\":\"unit\",\"title\":\"Synthetic tone\",\"artist\":\"Unit Artist\",\"duration\":1,\"webpage_url\":\"https://www.youtube.com/watch?v=unit\"}","");}
        int index=args.IndexOf("--output");string target=args[index+1].Replace("%(ext)s","webm");File.WriteAllText(target,"SYNTHETIC DOWNLOADED AUDIO");output?.Invoke("50.0%");return new(0,"","");
    }
}
sealed class FakeHttp:HttpMessageHandler
{
    public bool FailLyrics;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        string url=request.RequestUri!.ToString(),body;
        if(url.Contains("lrclib")){if(FailLyrics)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));body="{\"plainLyrics\":\"Synthetic test line\",\"syncedLyrics\":\"[00:00.00]Synthetic test line\",\"instrumental\":false}";}
        else if(url.Contains("open.spotify.com/embed"))body="<script id=\"__NEXT_DATA__\">{\"props\":{\"pageProps\":{\"state\":{\"data\":{\"entity\":{\"name\":\"Synthetic playlist\",\"trackList\":[{\"title\":\"Track A\",\"subtitle\":\"Artist A\",\"duration\":1000},{\"title\":\"Track B\",\"subtitle\":\"Artist B\",\"duration\":2000}]}}}}}}</script>";
        else if(url.Contains("/items"))body="{\"items\":[{\"item\":{\"name\":\"OAuth Synthetic\",\"uri\":\"spotify:track:oauth\",\"artists\":[{\"name\":\"Artist\"}]}}],\"next\":\"https://api.spotify.com/v1/playlists/OAUTH/items?limit=50\"}";
        else body="{\"name\":\"OAuth Test\"}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,"application/json")});
    }
}
sealed class BadToolsHttp:HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(Encoding.ASCII.GetBytes("NOT AN EXECUTABLE"))});}
