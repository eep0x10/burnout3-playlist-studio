using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Burnout3.PlaylistStudio.Core;
public sealed partial class PlaylistService
{
    private sealed record RemoteTrack(string Id,string Title,string Artist,string Album,string?YouTubeUrl,double?Duration);
    private sealed record RemoteListing(string?Name,List<RemoteTrack> Tracks,bool Complete,List<string> Warnings);
    private static (PlaylistSourceKind Kind,string Url,string Id) ParseSource(string url)
    {
        url=Required(url,2000,"Origem");
        if(IsPlaylistUrl(url)){var uri=new Uri(url);var match=Regex.Match(uri.Query,@"(?:[?&])list=([^&]+)");if(!match.Success)throw new ArgumentException("Playlist YouTube requer list=.");return(PlaylistSourceKind.YouTube,"https://www.youtube.com/playlist?list="+Uri.EscapeDataString(Uri.UnescapeDataString(match.Groups[1].Value)),match.Groups[1].Value);}
        if(Uri.TryCreate(url,UriKind.Absolute,out var spotify)&&spotify.Scheme=="https"&&spotify.Host=="open.spotify.com"&&string.IsNullOrEmpty(spotify.UserInfo))
        {var match=Regex.Match(spotify.AbsolutePath,@"^/(?:intl-[a-z]+/)?playlist/([A-Za-z0-9]+)/*$");if(match.Success)return(PlaylistSourceKind.Spotify,"https://open.spotify.com/playlist/"+match.Groups[1].Value,match.Groups[1].Value);}
        throw new ArgumentException("Informe uma URL de playlist YouTube ou Spotify.");
    }
    public Playlist BindPlaylistSource(Guid playlistId,string?url)
    {
        if(string.IsNullOrWhiteSpace(url))return State(d=>{var p=Find(d,playlistId);p.SourceUrl=null;p.SourceKind=PlaylistSourceKind.None;p.LastSyncComplete=null;p.SyncWarning=null;foreach(var t in p.Tracks)t.RemoteId=null;return Snapshot(p);},true);
        var source=ParseSource(url);return State(d=>{var p=Find(d,playlistId);p.AdoptExistingOnNextSync=p.SourceKind==PlaylistSourceKind.None;if(p.AdoptExistingOnNextSync)p.SourceAdoptionCandidates=p.Tracks.Where(t=>t.RemoteId==null).Select(t=>t.Id).ToList();p.SourceKind=source.Kind;p.SourceUrl=source.Url;p.LastSyncUtc=null;p.LastSyncComplete=null;p.SyncWarning="Origem vinculada. Atualize para obter a lista remota.";return Snapshot(p);},true);
    }
    private async Task<RemoteListing> FetchRemote(string url,string?token,CancellationToken ct)
    {
        var source=ParseSource(url);var tracks=new List<RemoteTrack>();var warnings=new List<string>();string?name=null;
        void Add(RemoteTrack track){if(tracks.Count>=500){if(!warnings.Contains("Limite de500 entradas; remoções suspensas."))warnings.Add("Limite de500 entradas; remoções suspensas.");return;}Required(track.Title,250,"Título remoto");Optional(track.Artist,250,"Artista remoto");Optional(track.Album,250,"Álbum remoto");tracks.Add(track);}
        if(source.Kind==PlaylistSourceKind.YouTube)
        {
            await EnsureToolsAsync(null,ct);
            var result=await Yt(["--dump-single-json","--flat-playlist","--skip-download","--ignore-errors","--playlist-end","501","--",source.Url],ct);
            using var json=JsonDocument.Parse(result.StandardOutput);name=Text(json.RootElement,"title");int enumerated=0;
            foreach(var item in json.RootElement.GetProperty("entries").EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();enumerated++;
                if(item.ValueKind!=JsonValueKind.Object){warnings.Add("Vídeo indisponível omitido; remoções suspensas.");continue;}
                var c=Candidate(item);string? availability=Text(item,"availability");
                if(string.IsNullOrEmpty(c.Id)||!IsYouTubeUrl(c.Url)||availability is "private" or "needs_auth" or "subscriber_only"||c.Title is "[Private video]" or "[Deleted video]"){warnings.Add("Entrada privada/indisponível omitida; remoções suspensas.");continue;}
                Add(new("youtube:"+c.Id,c.Title,c.Artist,"",c.Url,c.DurationSeconds));
            }
            double?total=Number(json.RootElement,"playlist_count")??Number(json.RootElement,"n_entries");
            bool complete=warnings.Count==0&&total!=null&&total==enumerated&&total<=500;
            if(!complete&&warnings.Count==0)warnings.Add("YouTube não confirmou a contagem completa; remoções suspensas.");
            return new(name,tracks,complete,warnings);
        }
        if(string.IsNullOrWhiteSpace(token))
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://open.spotify.com/embed/playlist/"+source.Id);request.Headers.UserAgent.ParseAdd("Burnout3PlaylistStudio/1.0");
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>4_000_000)throw new IOException("Página Spotify excede limite.");
            await using var stream=await response.Content.ReadAsStreamAsync(ct);using var bytes=new MemoryStream();var buffer=new byte[32768];int n;
            while((n=await stream.ReadAsync(buffer,ct))>0){if(bytes.Length+n>4_000_000)throw new IOException("Página Spotify excede limite.");bytes.Write(buffer,0,n);}
            string html=System.Text.Encoding.UTF8.GetString(bytes.ToArray());var script=Regex.Match(html,"<script[^>]*id=[\"']__NEXT_DATA__[\"'][^>]*>(.*?)</script>",RegexOptions.Singleline|RegexOptions.IgnoreCase);
            if(!script.Success)throw new InvalidDataException("Spotify não forneceu lista visível; biblioteca preservada.");
            using var json=JsonDocument.Parse(script.Groups[1].Value);var entity=json.RootElement.GetProperty("props").GetProperty("pageProps").GetProperty("state").GetProperty("data").GetProperty("entity");name=Text(entity,"name");
            foreach(var item in entity.GetProperty("trackList").EnumerateArray())
            {string?title=Text(item,"title");if(string.IsNullOrWhiteSpace(title)){warnings.Add("Entrada sem título omitida.");continue;}string artist=Text(item,"subtitle")??"";string id=Text(item,"uri")??"metadata:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title+"\n"+artist)));Add(new("spotify:"+id,title,artist,"",null,Number(item,"duration")/1000));}
            warnings.Add($"{tracks.Count} faixas visíveis; integralidade não confirmada. Spotify embed não comprova paginação completa; nenhuma faixa antiga será removida.");return new(name,tracks,false,warnings);
        }
        string? initialSnapshot;
        using(var meta=await GetJson("https://api.spotify.com/v1/playlists/"+source.Id,ct,token)){name=Text(meta.RootElement,"name");initialSnapshot=Text(meta.RootElement,"snapshot_id");}
        string?next="https://api.spotify.com/v1/playlists/"+source.Id+"/items?limit=50";var visited=new HashSet<string>();int pages=0;double? expectedTotal=null;int enumeratedItems=0;
        while(next!=null)
        {
            if(!visited.Add(next)||++pages>20){warnings.Add("Paginação repetida/limitada; remoções suspensas.");break;}
            var endpoint=new Uri(next);if(endpoint.Scheme!="https"||endpoint.Host!="api.spotify.com"||!string.IsNullOrEmpty(endpoint.UserInfo))throw new InvalidDataException("Paginação fora da API Spotify.");
            using var page=await GetJson(next,ct,token);double? pageTotal=Number(page.RootElement,"total");if(expectedTotal==null)expectedTotal=pageTotal;else if(pageTotal!=expectedTotal)warnings.Add("Contagem Spotify mudou durante paginação.");
            foreach(var entry in page.RootElement.GetProperty("items").EnumerateArray())
            {
                enumeratedItems++;JsonElement item;if((!entry.TryGetProperty("item",out item)&&!entry.TryGetProperty("track",out item))||item.ValueKind!=JsonValueKind.Object){warnings.Add("Entrada indisponível omitida.");continue;}
                string?title=Text(item,"name"),id=Text(item,"uri")??Text(item,"id");if((item.TryGetProperty("is_local",out var local)&&local.ValueKind==JsonValueKind.True)||(item.TryGetProperty("is_playable",out var playable)&&playable.ValueKind==JsonValueKind.False)||(Text(item,"type") is string type&&type!="track")||title==null||id==null){warnings.Add("Entrada sem identidade estável omitida.");continue;}
                string artist=item.TryGetProperty("artists",out var artists)?string.Join(", ",artists.EnumerateArray().Select(a=>Text(a,"name"))):"";string album=item.TryGetProperty("album",out var a)?Text(a,"name")??"":"";
                Add(new("spotify:"+id,title,artist,album,null,Number(item,"duration_ms")/1000));
            }
            next=Text(page.RootElement,"next");if(tracks.Count>=500&&next!=null){warnings.Add("Limite500 atingido antes do fim; remoções suspensas.");break;}
        }
        if(expectedTotal==null||expectedTotal!=enumeratedItems)warnings.Add("Contagem total Spotify não confirmada; remoções suspensas.");
        using(var meta=await GetJson("https://api.spotify.com/v1/playlists/"+source.Id,ct,token)){string? finalSnapshot=Text(meta.RootElement,"snapshot_id");if(initialSnapshot==null||finalSnapshot!=initialSnapshot)warnings.Add("Versão Spotify mudou/não foi confirmada durante a paginação; remoções suspensas.");}
        return new(name,tracks,next==null&&warnings.Count==0,warnings);
    }
    private async Task<ImportResult> ImportRemote(Guid id,string url,string?token,CancellationToken ct)
    {
        var source=ParseSource(url);var listing=await FetchRemote(source.Url,token,ct);ct.ThrowIfCancellationRequested();
        int added=State(d=>
        {
            var p=Find(d,id);if(p.SourceUrl!=null&&p.SourceUrl!=source.Url)throw new InvalidOperationException("Playlist já vinculada a outra origem. Use Fonte para trocar explicitamente.");
            if(p.Tracks.Count+listing.Tracks.Count>500)throw new ArgumentException("Importação ultrapassa500 faixas; biblioteca preservada.");
            p.SourceUrl=source.Url;p.SourceKind=source.Kind;p.LastSyncUtc=DateTime.UtcNow;p.LastSyncComplete=listing.Complete;p.SyncWarning=string.Join(" ",listing.Warnings);
            foreach(var item in listing.Tracks)p.Tracks.Add(NewRemote(item));if(d.Active?.PlaylistId==id)p.PendingActivation=true;return listing.Tracks.Count;
        },true);
        return new(added,listing.Complete,listing.Name,listing.Warnings);
    }
    private static Track NewRemote(RemoteTrack item)=>new(){RemoteId=item.Id,Title=item.Title,Artist=item.Artist,Album=item.Album,Source=item.YouTubeUrl??item.Title,YouTubeUrl=item.YouTubeUrl,DurationSeconds=item.Duration,Status=item.YouTubeUrl!=null?TrackStatus.Ready:TrackStatus.Added};
}
