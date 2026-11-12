using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Burnout3.PlaylistStudio.Core;

public sealed record TrackNotification(string Title,string Artist,string SourcePath,int ProcessId,DateTime DetectedUtc,string DetectionMethod="file-handle")
{ public Guid EventId {get;init;}=Guid.NewGuid(); }
public sealed record WatcherStatus(bool Available,string Message);

// Default: stable file-handle candidates; explicit optional log fallback never enables logging.
// An open event identifies a selected song, not pause, audibility, or race/menu context.
public sealed class NowPlayingWatcher(PlaylistService service,TimeSpan? pollInterval=null):IAsyncDisposable
{
    public event EventHandler<TrackNotification>? TrackChanged;
    public event EventHandler<WatcherStatus>? StatusChanged;
    private readonly CancellationTokenSource stop=new();private Task? running;private string? logPath;
    public void EnableDiagnosticLogFallback(string path)
    {logPath=Path.GetFullPath(path);}
    public Task StartAsync(CancellationToken cancellationToken=default)
    {lock(stop){return running??=Task.Run(()=>RunAsync(cancellationToken));}}
    private async Task RunAsync(CancellationToken token)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,stop.Token);
        var detector=new StableTrackDetector();var tail=new NewSongLogTail();string? lastStatus=null;DateTime refresh=DateTime.MinValue;
        IReadOnlyDictionary<string,TrackNotification> map=new Dictionary<string,TrackNotification>();
        void Status(bool available,string text){if(lastStatus==text)return;lastStatus=text;StatusChanged?.Invoke(this,new(available,text));}
        try
        {
            while(!linked.IsCancellationRequested)
            {
                try
                {
                    if(DateTime.UtcNow>=refresh){map=service.GetNowPlayingMap();refresh=DateTime.UtcNow.AddSeconds(3);}
                    string? path=logPath;
                    if(path==null)
                    {
                        var observations=new List<(int Pid,string Path)>();var processes=Process.GetProcessesByName("Takedawn");
                        try
                        {
                            foreach(var process in processes)
                                foreach(string source in PssAudioHandles.Read(process.Id,map.Keys))observations.Add((process.Id,source));
                            foreach(var item in detector.Observe(observations,DateTime.UtcNow))
                                if(map.TryGetValue(item.Path,out var metadata))TrackChanged?.Invoke(this,metadata with{ProcessId=item.Pid,DetectedUtc=DateTime.UtcNow,EventId=Guid.NewGuid()});
                            Status(true,map.Count==0?"Carregue uma playlist gerenciada para detectar faixas.":processes.Length==0?"Aguardando Takedawn. Nenhum diagnóstico do jogo foi ativado.":"Detectando MP3 aberto pelo Takedawn. Pausa e audibilidade não são verificadas.");
                        }
                        finally{foreach(var process in processes)process.Dispose();}
                    }
                    else
                    {
                        var events=tail.ReadNew(path);
                        // Log contents are never used to discover or open an arbitrary process.
                        var processes=Process.GetProcessesByName("Takedawn");
                        try
                        {
                            if(processes.Length==1&&map.Count>0)
                                foreach(string source in events)
                                    if(map.TryGetValue(source,out var metadata))TrackChanged?.Invoke(this,metadata with{ProcessId=processes[0].Id,DetectedUtc=DateTime.UtcNow,EventId=Guid.NewGuid(),DetectionMethod="diagnostic-log"});
                            Status(File.Exists(path),processes.Length>1?"Mais de um Takedawn aberto; identificação de faixa suspensa.":"Log autorizado: aguardando novos eventos de faixa. O diagnóstico deve estar ativado no Takedawn; pausa e audibilidade não são verificadas.");
                        }
                        finally{foreach(var process in processes)process.Dispose();}
                    }
                }
                catch(Exception ex)when(ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or ArgumentException)
                {detector.Observe([],DateTime.UtcNow);Status(false,"Detecção de faixa indisponível: "+ex.Message);}
                await Task.Delay(pollInterval??TimeSpan.FromSeconds(1),linked.Token);
            }
        }
        catch(OperationCanceledException)when(linked.IsCancellationRequested){}
    }
    public async ValueTask DisposeAsync(){stop.Cancel();if(running!=null)await running;stop.Dispose();}
}

internal sealed class StableTrackDetector
{
    private readonly Dictionary<(int Pid,string Path),DateTime> firstSeen=[];
    private readonly HashSet<(int Pid,string Path)> emitted=[];
    internal IReadOnlyList<(int Pid,string Path)> Observe(IEnumerable<(int Pid,string Path)> values,DateTime now)
    {
        var present=values.ToHashSet();foreach(var key in firstSeen.Keys.Where(k=>!present.Contains(k)).ToArray()){firstSeen.Remove(key);emitted.Remove(key);}
        foreach(var key in present)firstSeen.TryAdd(key,now);
        List<(int Pid,string Path)> result=[];
        foreach(var group in present.GroupBy(p=>p.Pid))
        {
            // Multiple decoder/file handles are ambiguous (transition/scanner); wait.
            if(group.Count()!=1)continue;var key=group.Single();
            if(now-firstSeen[key]>=TimeSpan.FromMilliseconds(750)&&emitted.Add(key))result.Add(key);
        }
        return result;
    }
}
internal sealed class NewSongLogTail
{
    private string? current;private long position;private DateTime creation;private string pending="";
    private static readonly Regex Song=new(@"^\[SOUNDTRACK\] song [0-9A-Fa-f]{8}: (?<path>.+) \(\d+ Hz, \d+ ch\)\r?$",RegexOptions.Compiled|RegexOptions.CultureInvariant);
    internal IReadOnlyList<string> ReadNew(string path)
    {
        if(!File.Exists(path))return [];
        var info=new FileInfo(path);
        // Attach at EOF and discard on replacement/truncation: never replay a previous run.
        if(current!=path||creation!=info.CreationTimeUtc||info.Length<position)
        {current=path;creation=info.CreationTimeUtc;position=info.Length;pending="";return [];}
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        stream.Position=position;
        if(stream.Length-position>1024*1024){position=stream.Length;pending="";return [];}
        using var reader=new StreamReader(stream,Encoding.UTF8,true,4096,true);
        string text=pending+reader.ReadToEnd();position=stream.Position;var lines=text.Split('\n');pending=lines[^1];if(pending.Length>32768)pending="";
        List<string> result=[];
        foreach(string line in lines[..^1]){var match=Song.Match(line);if(!match.Success)continue;try{result.Add(Path.GetFullPath(match.Groups["path"].Value));}catch(ArgumentException){}}
        return result;
    }
}

public sealed partial class PlaylistService
{
    internal IReadOnlyDictionary<string,TrackNotification> GetNowPlayingMap()
    {
        return State(d=>
        {
            var map=new Dictionary<string,TrackNotification>(StringComparer.OrdinalIgnoreCase);var active=d.Active;if(active==null)return map;
            var playlist=d.Playlists.FirstOrDefault(p=>p.Id==active.PlaylistId);if(playlist==null)return map;
            var manifest=ReadActiveManifest(active);
            foreach(var file in manifest.Files.Where(x=>x.Key.EndsWith(".mp3",StringComparison.OrdinalIgnoreCase)))
            {
                ValidateManagedName(active.MusicRoot,file.Key,file.Value.TrackId);
                var track=playlist.Tracks.FirstOrDefault(t=>t.Id==file.Value.TrackId);if(track==null)continue;
                string path=SafeFiles.Within(active.MusicRoot,Path.Combine(active.MusicRoot,file.Key));
                if(File.Exists(path))map[path]=new(track.Title,track.Artist,path,0,DateTime.UtcNow);
            }
            return map;
        });
    }
}
