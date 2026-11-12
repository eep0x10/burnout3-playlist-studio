using System.Globalization;
using System.Text.RegularExpressions;

namespace Burnout3.PlaylistStudio.Core;

public sealed partial class PlaylistService
{
    public Task<OperationResult> ProcessPlaylistAsync(Guid playlistId,IProgress<OperationProgress>?progress=null,CancellationToken cancellationToken=default)
        =>Process(playlistId,null,progress,cancellationToken);
    public Task<OperationResult> ProcessTrackAsync(Guid playlistId,Guid trackId,IProgress<OperationProgress>?progress=null,CancellationToken cancellationToken=default)
        =>Process(playlistId,trackId,progress,cancellationToken);
    private async Task<OperationResult> Process(Guid playlistId,Guid?trackId,IProgress<OperationProgress>?progress,CancellationToken token)
    {
        await Worker.WaitAsync(token);int done=0,failed=0,cancelled=0;var warnings=new List<string>();
        FileStream? workerLock=null;
        try
        {
            try{workerLock=new FileStream(Path.Combine(DataDirectory,".worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException){throw new IOException("A biblioteca já está sendo processada por outra instância.");}
            State(d=>{foreach(var job in d.Jobs.Where(j=>j.Status is JobStatus.Running or JobStatus.Queued)){job.Status=JobStatus.Cancelled;job.Error="Operação interrompida anteriormente; retome manualmente.";job.FinishedUtc=DateTime.UtcNow;}return 0;},true);
            var tracks=State(d=>Find(d,playlistId).Tracks.Where(t=>trackId==null||t.Id==trackId).Select(t=>t.Id).ToArray());if(trackId!=null&&tracks.Length==0)throw new KeyNotFoundException("Faixa não encontrada.");
            await EnsureToolsAsync(progress,token);
            foreach(Guid id in tracks)
            {
                if(token.IsCancellationRequested){cancelled++;break;}
                var job=new DownloadJob{PlaylistId=playlistId,TrackId=id};State(d=>{d.Jobs.Add(job);return 0;},true);
                try
                {
                    for(int attempt=1;;attempt++)
                    {
                        State(d=>{var j=d.Jobs.First(j=>j.Id==job.Id);j.Status=JobStatus.Running;j.Attempts=attempt;return 0;},true);
                        try{await ProcessOne(playlistId,id,progress,token);break;}
                        catch(IOException)when(attempt<2&&!token.IsCancellationRequested){await Task.Delay(1000,token);}
                    }
                    State(d=>{var j=d.Jobs.First(j=>j.Id==job.Id);j.Status=JobStatus.Completed;j.FinishedUtc=DateTime.UtcNow;return 0;},true);done++;
                }
                catch(OperationCanceledException)
                {State(d=>{var j=d.Jobs.First(j=>j.Id==job.Id);j.Status=JobStatus.Cancelled;j.FinishedUtc=DateTime.UtcNow;var t=Find(d,playlistId,id);if(t.Status!=TrackStatus.Downloaded)t.Status=TrackStatus.Cancelled;return 0;},true);cancelled++;break;}
                catch(Exception ex)
                {State(d=>{var j=d.Jobs.First(j=>j.Id==job.Id);j.Status=JobStatus.Failed;j.Error=ex.Message;j.FinishedUtc=DateTime.UtcNow;var t=Find(d,playlistId,id);t.Status=TrackStatus.Failed;t.Error=ex.Message;return 0;},true);failed++;warnings.Add(ex.Message);}
            }
        }
        finally{workerLock?.Dispose();Worker.Release();}
        return new(done,failed,cancelled,warnings);
    }
    private async Task ProbeDuration(Guid playlistId,Guid trackId,string file,CancellationToken token)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var result=await runner.RunAsync(GetTools().FfprobePath,["-v","error","-show_entries","format=duration","-of","default=noprint_wrappers=1:nokey=1","-i",file],null,timeout.Token);
            if(result.ExitCode==0&&double.TryParse(result.StandardOutput.Trim(),NumberStyles.Float,CultureInfo.InvariantCulture,out var seconds)&&double.IsFinite(seconds)&&seconds>0)
                State(d=>{Find(d,playlistId,trackId).DurationSeconds=seconds;return 0;},true);
        }
        catch(OperationCanceledException)when(!token.IsCancellationRequested){} // Optional metadata timeout must not discard otherwise-valid audio.
        catch(IOException){}
    }
    private async Task ProcessOne(Guid playlistId,Guid trackId,IProgress<OperationProgress>?progress,CancellationToken token)
    {
        var snapshot=State(d=>Find(d,playlistId,trackId) with{});string final=AudioPath(playlistId,trackId);
        if(File.Exists(final)&&!snapshot.MetadataDirty)
        {if(snapshot.DurationSeconds==null)await ProbeDuration(playlistId,trackId,final,token);if(snapshot.LyricsStatus is LyricsStatus.Pending or LyricsStatus.Failed)await FetchLyrics(playlistId,trackId,token);RecordManagedFiles(playlistId,trackId);return;}
        string stage=SafeFiles.Within(DataDirectory,Path.Combine(DataDirectory,"staging",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(stage);
        try
        {
            string input;
            if(File.Exists(final)){input=final;}
            else if(Path.IsPathFullyQualified(snapshot.Source)&&!IsYouTubeUrl(snapshot.Source))
            {input=snapshot.Source;if(!File.Exists(input))throw new IOException("Arquivo local original não foi encontrado.");}
            else
            {
                progress?.Report(new(playlistId,trackId,"resolve",null,"Buscando correspondência no YouTube…"));snapshot=await ResolveTrackAsync(playlistId,trackId,token);
                State(d=>{Find(d,playlistId,trackId).Status=TrackStatus.Downloading;return 0;},true);
                void Output(string line){var match=Regex.Match(line,"(?<n>[0-9]+(?:\\.[0-9]+)?)%");double?percent=match.Success?double.Parse(match.Groups["n"].Value,CultureInfo.InvariantCulture):null;progress?.Report(new(playlistId,trackId,"download",percent,line.Length>250?line[..250]:line));}
                await Yt(["--no-playlist","--no-part","--newline","--max-filesize","250M","--match-filter","duration <= 1800","--format","bestaudio/best","--output",Path.Combine(stage,"source.%(ext)s"),"--",snapshot.YouTubeUrl!],token,Output);
                input=Directory.EnumerateFiles(stage,"source.*").Where(p=>!p.EndsWith(".part",StringComparison.OrdinalIgnoreCase)&&!p.EndsWith(".ytdl",StringComparison.OrdinalIgnoreCase)).SingleOrDefault()??throw new IOException("O download não gerou áudio (vídeo indisponível/limite de30min ou250MB).");
                if(new FileInfo(input).Length>260_000_000)throw new IOException("Áudio excede limite de tamanho.");
            }
            if(snapshot.DurationSeconds==null)await ProbeDuration(playlistId,trackId,input,token);
            snapshot=State(d=>Find(d,playlistId,trackId) with{});string playlistName=State(d=>Find(d,playlistId).Name);var tools=GetTools();string output=Path.Combine(stage,"ready.mp3");
            var args=new List<string>{"-nostdin","-hide_banner","-loglevel","error","-y","-i",input,"-vn"};
            if(Path.GetExtension(input).Equals(".mp3",StringComparison.OrdinalIgnoreCase))args.AddRange(["-c:a","copy"]);else args.AddRange(["-c:a","libmp3lame","-b:a","192k"]);
            args.AddRange(["-metadata","title="+snapshot.Title,"-metadata","artist="+snapshot.Artist,"-metadata","album="+(string.IsNullOrWhiteSpace(snapshot.Album)?playlistName:snapshot.Album),"-id3v2_version","3",output]);
            progress?.Report(new(playlistId,trackId,"convert",null,"Preparando MP3 e metadados…"));using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromMinutes(10));
            var converted=await runner.RunAsync(tools.FfmpegPath,args,null,limit.Token);if(converted.ExitCode!=0||!File.Exists(output)||new FileInfo(output).Length==0)throw new IOException("FFmpeg falhou: "+Tail(converted.StandardError));
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);if(File.Exists(final))File.Replace(output,final,final+".bak");else File.Move(output,final);
            State(d=>{var t=Find(d,playlistId,trackId);t.Status=TrackStatus.Downloaded;t.Error=null;t.MetadataDirty=false;return 0;},true);
            RecordManagedFiles(playlistId,trackId);
            progress?.Report(new(playlistId,trackId,"lyrics",null,"Consultando letra (falha não impede o MP3)…"));await FetchLyrics(playlistId,trackId,token);RecordManagedFiles(playlistId,trackId);
            progress?.Report(new(playlistId,trackId,"done",100,"MP3 pronto."));
        }
        finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
}
