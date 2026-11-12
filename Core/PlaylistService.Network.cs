using System.Globalization;

using System.Net;

using System.Text.Json;

using System.Text.RegularExpressions;



namespace Burnout3.PlaylistStudio.Core;



public sealed partial class PlaylistService

{

    private async Task<JsonDocument> GetJson(string url,CancellationToken token,string?bearer=null)

    {

        for(int attempt=0;;attempt++)

        {

            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("Burnout3PlaylistStudio/1.0 (+https://github.com/eep0x10)");

            if(bearer!=null)request.Headers.Authorization=new("Bearer",bearer);

            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);

            if((response.StatusCode==HttpStatusCode.TooManyRequests||response.StatusCode==HttpStatusCode.ServiceUnavailable)&&attempt<2)

            {double seconds=response.Headers.RetryAfter?.Delta?.TotalSeconds??(attempt+1)*2;await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(seconds,1,30)),token);continue;}

            response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>16_000_000)throw new IOException("Resposta remota excede limite de metadados.");

            await using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[32768];int n;

            while((n=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+n>16_000_000)throw new IOException("Resposta remota excede limite de metadados.");bytes.Write(buffer,0,n);}

            return JsonDocument.Parse(bytes.ToArray());

        }

    }

    private static string? Text(JsonElement json,string property)=>json.TryGetProperty(property,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;

    private static double? Number(JsonElement json,string property)
    {
        if(!json.TryGetProperty(property,out var value))return null;
        if(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number))return double.IsFinite(number)?number:null;
        if(value.ValueKind==JsonValueKind.String&&double.TryParse(value.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out number))return double.IsFinite(number)?number:null;
        return null;
    }

    private async Task<ToolResult> Yt(IReadOnlyList<string> args,CancellationToken token,Action<string>?output=null)

    {

        var tools=GetTools();if(!tools.Ready)throw new IOException("Prepare as ferramentas antes de pesquisar/baixar: "+string.Join(", ",tools.Missing));

        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(15));

        var arguments=new List<string>{"--ignore-config","--no-warnings","--no-color","--socket-timeout","30","--retries","2","--fragment-retries","2"};

        if(tools.DenoPath!=null){arguments.Add("--js-runtimes");arguments.Add("deno:"+tools.DenoPath);}

        arguments.AddRange(args);var result=await runner.RunAsync(tools.YtDlpPath,arguments,output,timeout.Token);

        if(result.ExitCode!=0)throw new IOException("yt-dlp falhou: "+Tail(result.StandardError));return result;

    }

    private static string Tail(string text)=>text.Length>1500?text[^1500..]:text;

    private static YouTubeCandidate Candidate(JsonElement item)

    {

        string id=Text(item,"id")??"";string url=Text(item,"webpage_url")??Text(item,"url")??"";

        if(!IsYouTubeUrl(url)&&id.Length>0)url="https://www.youtube.com/watch?v="+Uri.EscapeDataString(id);

        return new(id,Text(item,"track")??Text(item,"title")??id,Text(item,"artist")??Text(item,"creator")??Text(item,"uploader")??Text(item,"channel")??"",url,Number(item,"duration"),Text(item,"thumbnail"));

    }

    public async Task<IReadOnlyList<YouTubeCandidate>> SearchYouTubeAsync(string query,CancellationToken cancellationToken=default)

    {

        query=Required(query,1000,"Pesquisa");var result=await Yt(["--dump-single-json","--flat-playlist","--skip-download","--playlist-end","5","--","ytsearch5:"+query],cancellationToken);

        using var json=JsonDocument.Parse(result.StandardOutput);return json.RootElement.GetProperty("entries").EnumerateArray().Where(i=>i.ValueKind==JsonValueKind.Object).Select(Candidate).Where(c=>IsYouTubeUrl(c.Url)).OrderByDescending(c=>Match(query,c.Title+" "+c.Artist)).ToArray();

    }

    private static double Match(string query,string candidate)

    {

        string Normalize(string s)=>Regex.Replace(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD),"[\\p{Mn}]","");

        var words=Regex.Matches(Normalize(query),"[\\p{L}\\p{Nd}]+").Select(m=>m.Value).Where(w=>w.Length>1).Distinct().ToArray();

        string normalized=Normalize(candidate);double score=words.Length==0?0:words.Count(normalized.Contains)/(double)words.Length;

        foreach(string bad in new[]{"cover","karaoke","live","slowed","sped up","tutorial","reaction"})if(normalized.Contains(bad)&&!Normalize(query).Contains(bad))score-=0.15;

        if(normalized.Contains("official"))score+=0.02;return score;

    }

    public Track SelectCandidate(Guid playlistId,Guid trackId,YouTubeCandidate candidate)

    {

        if(!IsYouTubeUrl(candidate.Url)||IsPlaylistUrl(candidate.Url))throw new ArgumentException("Selecione um vídeo individual do YouTube.");

        return State(d=>{var t=Find(d,playlistId,trackId);t.YouTubeUrl=candidate.Url;t.DurationSeconds=candidate.DurationSeconds;t.ArtworkUrl=candidate.Thumbnail;if(IsYouTubeUrl(t.Title))t.Title=candidate.Title;if(string.IsNullOrWhiteSpace(t.Artist))t.Artist=candidate.Artist.Replace(" - Topic","",StringComparison.OrdinalIgnoreCase);t.Status=TrackStatus.Ready;t.Error=null;return Snapshot(playlistId,t);},true);

    }

    public async Task<Track> ResolveTrackAsync(Guid playlistId,Guid trackId,CancellationToken cancellationToken=default)

    {

        var track=State(d=>Find(d,playlistId,trackId) with{});

        if(Path.IsPathFullyQualified(track.Source)&&File.Exists(track.Source))return Snapshot(playlistId,track);

        State(d=>{Find(d,playlistId,trackId).Status=TrackStatus.Resolving;return 0;},true);

        try

        {

            YouTubeCandidate candidate;

            if(IsYouTubeUrl(track.YouTubeUrl??track.Source))

            {var result=await Yt(["--dump-single-json","--skip-download","--no-playlist","--",track.YouTubeUrl??track.Source],cancellationToken);using var json=JsonDocument.Parse(result.StandardOutput);candidate=Candidate(json.RootElement);}

            else candidate=(await SearchYouTubeAsync(track.Artist+" "+track.Title,cancellationToken)).FirstOrDefault()??throw new IOException("Nenhum resultado do YouTube. Revise título e artista.");

            return SelectCandidate(playlistId,trackId,candidate);

        }

        catch(OperationCanceledException){State(d=>{Find(d,playlistId,trackId).Status=TrackStatus.Cancelled;return 0;},true);throw;}

        catch(Exception ex){State(d=>{var t=Find(d,playlistId,trackId);t.Status=TrackStatus.Failed;t.Error=ex.Message;return 0;},true);throw;}

    }

    public Task<ImportResult> ImportYouTubePlaylistAsync(Guid playlistId,string playlistUrl,CancellationToken cancellationToken=default)
        =>ImportRemote(playlistId,playlistUrl,null,cancellationToken);
    public Task<ImportResult> ImportSpotifyAsync(Guid playlistId,string playlistUrl,string?accessToken=null,CancellationToken cancellationToken=default)
        =>ImportRemote(playlistId,playlistUrl,accessToken,cancellationToken);

    private async Task FetchLyrics(Guid playlistId,Guid trackId,CancellationToken token)

    {

        var track=State(d=>Find(d,playlistId,trackId) with{});

        try

        {

            string query="track_name="+Uri.EscapeDataString(track.Title)+"&artist_name="+Uri.EscapeDataString(track.Artist);

            if(track.DurationSeconds is double duration)query+="&duration="+duration.ToString("0",CultureInfo.InvariantCulture);

            if(track.Album.Length>0)query+="&album_name="+Uri.EscapeDataString(track.Album);

            JsonElement lyrics;JsonDocument? found=null;

            try{found=await GetJson("https://lrclib.net/api/get?"+query,token);lyrics=found.RootElement;}

            catch(HttpRequestException ex)when(ex.StatusCode==HttpStatusCode.NotFound)

            {

                await Task.Delay(350,token);found=await GetJson("https://lrclib.net/api/search?track_name="+Uri.EscapeDataString(track.Title)+"&artist_name="+Uri.EscapeDataString(track.Artist),token);

                var candidates=found.RootElement.EnumerateArray().Where(l=>Match(track.Title,Text(l,"trackName")??"")>=0.6&&Match(track.Artist,Text(l,"artistName")??"")>=0.5&&(track.DurationSeconds==null||Number(l,"duration")==null||Math.Abs(Number(l,"duration")!.Value-track.DurationSeconds.Value)<=3)).ToArray();

                if(candidates.Length==0){found.Dispose();State(d=>{Find(d,playlistId,trackId).LyricsStatus=LyricsStatus.NotFound;return 0;},true);return;}lyrics=candidates.FirstOrDefault(l=>!string.IsNullOrWhiteSpace(Text(l,"syncedLyrics")));if(lyrics.ValueKind==JsonValueKind.Undefined)lyrics=candidates[0];

            }

            using(found)

            {

                string? synced=Text(lyrics,"syncedLyrics"),plain=Text(lyrics,"plainLyrics");bool instrumental=lyrics.TryGetProperty("instrumental",out var flag)&&flag.ValueKind==JsonValueKind.True;

                if(!string.IsNullOrWhiteSpace(synced))SafeFiles.Write(SidecarPath(playlistId,trackId,"lrc"),System.Text.Encoding.UTF8.GetBytes(synced));

                if(!string.IsNullOrWhiteSpace(plain))SafeFiles.Write(SidecarPath(playlistId,trackId,"txt"),System.Text.Encoding.UTF8.GetBytes(plain));

                State(d=>{var t=Find(d,playlistId,trackId);t.LyricsStatus=instrumental?LyricsStatus.Instrumental:!string.IsNullOrWhiteSpace(synced)?LyricsStatus.Synced:!string.IsNullOrWhiteSpace(plain)?LyricsStatus.Plain:LyricsStatus.NotFound;t.LyricsError=null;return 0;},true);

            }

        }

        catch(OperationCanceledException){throw;}

        catch(Exception ex){State(d=>{var t=Find(d,playlistId,trackId);t.LyricsStatus=LyricsStatus.Failed;t.LyricsError=ex is HttpRequestException h?"LRCLIB indisponível: "+h.StatusCode:ex.Message;return 0;},true);}

        RecordManagedFiles(playlistId,trackId);
        await Task.Delay(350,token);

    }

}
