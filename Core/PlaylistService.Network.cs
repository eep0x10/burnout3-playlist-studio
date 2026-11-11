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

    public async Task<ImportResult> ImportYouTubePlaylistAsync(Guid playlistId,string playlistUrl,CancellationToken cancellationToken=default)

    {

        if(!IsPlaylistUrl(playlistUrl))throw new ArgumentException("Informe uma playlist explícita do YouTube.");

        var result=await Yt(["--dump-single-json","--flat-playlist","--skip-download","--ignore-errors","--playlist-end","500","--",playlistUrl],cancellationToken);

        using var json=JsonDocument.Parse(result.StandardOutput);var warnings=new List<string>();int added=0;

        foreach(var item in json.RootElement.GetProperty("entries").EnumerateArray())

        {

            if(item.ValueKind!=JsonValueKind.Object){warnings.Add("Vídeo privado/indisponível omitido.");continue;}

            var candidate=Candidate(item);if(!IsYouTubeUrl(candidate.Url)){warnings.Add("Entrada sem vídeo acessível omitida.");continue;}

            try{var track=AddTrack(playlistId,candidate.Url,candidate.Artist);UpdateTrack(playlistId,track.Id,candidate.Title,candidate.Artist);SelectCandidate(playlistId,track.Id,candidate);added++;}catch(ArgumentException ex){warnings.Add(ex.Message);break;}

        }

        if(Number(json.RootElement,"playlist_count") is double total&&total>added)warnings.Add($"Importação parcial: {added} de {total:0} entradas anunciadas; limite500 ou entradas indisponíveis.");

        return new(added,warnings.Count==0,Text(json.RootElement,"title"),warnings);

    }

    public async Task<ImportResult> ImportSpotifyAsync(Guid playlistId,string playlistUrl,string?accessToken=null,CancellationToken cancellationToken=default)

    {

        if(!Uri.TryCreate(playlistUrl,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="open.spotify.com")throw new ArgumentException("Informe uma playlist pública open.spotify.com.");

        var match=Regex.Match(uri.AbsolutePath,"/playlist/([A-Za-z0-9]+)");if(!match.Success)throw new ArgumentException("Link Spotify não é uma playlist.");string id=match.Groups[1].Value;

        if(string.IsNullOrWhiteSpace(accessToken))
        {
            string? name=null;int visible=0;
            try
            {
                using var request=new HttpRequestMessage(HttpMethod.Get,$"https://open.spotify.com/embed/playlist/{id}");request.Headers.UserAgent.ParseAdd("Burnout3PlaylistStudio/1.0");
                using var response=await http.SendAsync(request,cancellationToken);response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>4_000_000)throw new IOException("Página Spotify excede limite de metadados.");
                string html=await response.Content.ReadAsStringAsync(cancellationToken);if(html.Length>4_000_000)throw new IOException("Página Spotify excede limite de metadados.");
                var script=Regex.Match(html,"<script[^>]*id=[\"']__NEXT_DATA__[\"'][^>]*>(.*?)</script>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
                if(!script.Success)throw new InvalidDataException("Página pública não forneceu lista visível.");
                using var embed=JsonDocument.Parse(script.Groups[1].Value);var entity=embed.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("state").GetProperty("data").GetProperty("entity");
                name=Text(entity,"name");
                foreach(var item in entity.GetProperty("trackList").EnumerateArray().Take(500))
                {
                    string? title=Text(item,"title");if(string.IsNullOrWhiteSpace(title))continue;
                    var track=AddTrack(playlistId,title,Text(item,"subtitle"));
                    double? duration=Number(item,"duration");if(duration!=null)State(d=>{Find(d,playlistId,track.Id).DurationSeconds=duration/1000;return 0;},true);visible++;
                }
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex)when(ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidDataException or ArgumentException)
            {return new(visible,false,name,[$"{visible} faixas visíveis importadas; integralidade não confirmada. Embed indisponível/alterado ou limite atingido. Use CSV/texto ou token oficial com acesso à playlist. Nenhum áudio Spotify é baixado."]);}
            return new(visible,false,name,[$"{visible} faixas visíveis encontradas; integralidade não confirmada: a página embed pode limitar/omitir entradas e não oferece paginação confiável. Para uma lista conhecida, use CSV/texto ou API oficial autorizada. Áudio será resolvido no YouTube, não no preview Spotify."]);
        }

        int added=0,skipped=0;var warnings=new List<string>();string? nameResult=null;

        try

        {

            using(var metadata=await GetJson($"https://api.spotify.com/v1/playlists/{id}",cancellationToken,accessToken))nameResult=Text(metadata.RootElement,"name");

            string? next=$"https://api.spotify.com/v1/playlists/{id}/items?limit=50";var visited=new HashSet<string>();

            while(next!=null&&visited.Add(next))

            {

                var endpoint=new Uri(next);if(endpoint.Scheme!="https"||endpoint.Host!="api.spotify.com")throw new InvalidDataException("Paginação Spotify fora da API oficial.");

                using var page=await GetJson(next,cancellationToken,accessToken);

                foreach(var entry in page.RootElement.GetProperty("items").EnumerateArray())

                {

                    JsonElement item;if(!entry.TryGetProperty("item",out item)&&!entry.TryGetProperty("track",out item)||item.ValueKind!=JsonValueKind.Object){skipped++;continue;}

                    string? title=Text(item,"name");if(title==null){skipped++;continue;}

                    string artists=item.TryGetProperty("artists",out var artistList)?string.Join(", ",artistList.EnumerateArray().Select(a=>Text(a,"name")).Where(a=>a!=null)):"";

                    try{var track=AddTrack(playlistId,title,artists);if(item.TryGetProperty("album",out var album))UpdateTrack(playlistId,track.Id,title,artists,Text(album,"name")??"");added++;}catch(ArgumentException ex){warnings.Add(ex.Message);next=null;break;}

                }

                if(warnings.Count>0)break;next=Text(page.RootElement,"next");

            }

            if(next!=null&&visited.Contains(next))warnings.Add("Paginação Spotify repetida/interrompida; integralidade não confirmada.");

            if(skipped>0)warnings.Add($"{skipped} entradas locais/privadas/indisponíveis omitidas.");

        }

        catch(HttpRequestException ex){warnings.Add($"Spotify API recusou ou interrompeu o acesso ({ex.StatusCode}). Em modo de desenvolvimento, acesso pode se limitar a playlists próprias/colaborativas. Use CSV/texto; importação parcial não é uma lista completa.");}

        return new(added,warnings.Count==0,nameResult,warnings);

    }

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

        await Task.Delay(350,token);

    }

}
