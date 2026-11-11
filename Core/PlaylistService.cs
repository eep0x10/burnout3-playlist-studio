using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualBasic.FileIO;

namespace Burnout3.PlaylistStudio.Core;

public sealed partial class PlaylistService
{
    private sealed class LibraryData
    {
        public int Version {get;set;}=1;
        public List<Playlist> Playlists {get;set;}=[];
        public List<DownloadJob> Jobs {get;set;}=[];
        public string TakedawnRoot {get;set;}=DefaultMusicRoot;
        public ActivationRecord? Active {get;set;}
    }
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    private static readonly HttpClient DefaultHttp=new(){Timeout=TimeSpan.FromMinutes(10)};
    private static readonly SemaphoreSlim Worker=new(1,1);
    private readonly string libraryPath,toolsDirectory;
    private readonly IToolRunner runner;
    private readonly HttpClient http;
    private readonly object stateGate=new();
    public string DataDirectory {get;}
    public string ToolsDirectory=>toolsDirectory;
    public static string DefaultMusicRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) is {Length:>0} music?music:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Music"),"Takedawn");
    public PlaylistService(string?dataDirectory=null,IToolRunner?runner=null,HttpClient?http=null,string?toolsDirectory=null)
    {
        DataDirectory=Path.GetFullPath(dataDirectory??Path.Combine(AppContext.BaseDirectory,"data"));Directory.CreateDirectory(DataDirectory);
        libraryPath=Path.Combine(DataDirectory,"library.json");this.toolsDirectory=Path.GetFullPath(toolsDirectory??Path.Combine(Directory.GetParent(DataDirectory)!.FullName,"tools"));
        this.runner=runner??new ProcessToolRunner();this.http=http??DefaultHttp;
    }
    private T State<T>(Func<LibraryData,T> action,bool write=false)
    {
        lock(stateGate)
        {
            string key=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(libraryPath.ToUpperInvariant())));
            using var mutex=new Mutex(false,@"Local\BurnoutPlaylist-"+key);bool owned=false;
            try
            {
                try{owned=mutex.WaitOne(TimeSpan.FromSeconds(15));}catch(AbandonedMutexException){owned=true;}
                if(!owned)throw new IOException("Outra instância está usando a biblioteca.");
                LibraryData data;
                try{data=File.Exists(libraryPath)?JsonSerializer.Deserialize<LibraryData>(File.ReadAllBytes(libraryPath),Json)??throw new JsonException():new();}
                catch(JsonException ex){throw new InvalidDataException("Biblioteca JSON inválida; preserve o arquivo e recupere library.json.bak.",ex);}
                if(data.Version!=1||data.Playlists.Any(p=>p.Id==Guid.Empty||p.Tracks.Any(t=>t.Id==Guid.Empty))||data.Playlists.Select(p=>p.Id).Distinct().Count()!=data.Playlists.Count||data.Playlists.SelectMany(p=>p.Tracks).Select(t=>t.Id).Distinct().Count()!=data.Playlists.Sum(p=>p.Tracks.Count))throw new InvalidDataException("Estrutura da biblioteca inválida.");
                var result=action(data);
                if(write){if(data.Jobs.Count>500)data.Jobs=data.Jobs.TakeLast(500).ToList();SafeFiles.Write(libraryPath,JsonSerializer.SerializeToUtf8Bytes(data,Json));}
                return result;
            }
            finally{if(owned)mutex.ReleaseMutex();}
        }
    }
    private static Playlist Find(LibraryData data,Guid id)=>data.Playlists.FirstOrDefault(p=>p.Id==id)??throw new KeyNotFoundException("Playlist não encontrada.");
    private static Track Find(LibraryData data,Guid playlist,Guid track)=>Find(data,playlist).Tracks.FirstOrDefault(t=>t.Id==track)??throw new KeyNotFoundException("Faixa não encontrada.");
    private string AudioPath(Guid playlist,Guid track)=>SafeFiles.Within(DataDirectory,Path.Combine(DataDirectory,"audio",playlist.ToString("N"),track.ToString("N")+".mp3"));
    private string SidecarPath(Guid playlist,Guid track,string extension)=>Path.ChangeExtension(AudioPath(playlist,track),extension);
    private Track Snapshot(Guid playlist,Track t)
    {
        string audio=AudioPath(playlist,t.Id),lrc=SidecarPath(playlist,t.Id,"lrc"),txt=SidecarPath(playlist,t.Id,"txt");
        return t with{LocalPath=File.Exists(audio)?audio:null,LyricsPath=File.Exists(lrc)?lrc:File.Exists(txt)?txt:null};
    }
    private Playlist Snapshot(Playlist p)=>p with{Tracks=p.Tracks.Select(t=>Snapshot(p.Id,t)).ToList()};
    public LibrarySnapshot GetLibrary()=>State(d=>new LibrarySnapshot(d.Playlists.Select(Snapshot).ToList(),d.Jobs.Select(j=>j with{}).ToList(),d.TakedawnRoot){ActivePlaylistId=d.Active?.PlaylistId});
    public LibrarySnapshot SetMusicRoot(string path)
    {
        path=Path.GetFullPath(Required(path,2000,"Pasta de música"));
        State(d=>{d.TakedawnRoot=path;return 0;},true);return GetLibrary();
    }
    public Playlist CreatePlaylist(string name)=>State(d=>{name=Required(name,120,"Nome");if(d.Playlists.Count>=100)throw new ArgumentException("Limite de 100 playlists do Takedawn.");var p=new Playlist{Name=name};d.Playlists.Add(p);return Snapshot(p);},true);
    public Playlist RenamePlaylist(Guid playlistId,string name)=>State(d=>{var p=Find(d,playlistId);p.Name=Required(name,120,"Nome");return Snapshot(p);},true);
    public void DeletePlaylist(Guid playlistId)=>State(d=>{var p=Find(d,playlistId);d.Playlists.Remove(p);d.Jobs.RemoveAll(j=>j.PlaylistId==playlistId);return 0;},true);
    public Track AddTrack(Guid playlistId,string queryOrUrl,string?artist=null)=>State(d=>
    {
        var p=Find(d,playlistId);if(p.Tracks.Count>=500)throw new ArgumentException("Limite de 500 faixas por playlist.");string source=Required(queryOrUrl,2000,"Faixa");
        if(Uri.TryCreate(source,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"&&!IsYouTube(uri))throw new ArgumentException("Use um link do YouTube ou o nome da faixa; Spotify tem importador próprio.");
        var track=new Track{Title=source,Artist=Optional(artist,250,"Artista"),Source=source};
        if(IsYouTubeUrl(source)){if(IsPlaylistUrl(source))throw new ArgumentException("Use o importador de playlist para links com list=.");track.YouTubeUrl=source;track.Status=TrackStatus.Ready;}
        p.Tracks.Add(track);return Snapshot(p.Id,track);
    },true);
    public Track UpdateTrack(Guid playlistId,Guid trackId,string title,string artist,string album="")=>State(d=>{var t=Find(d,playlistId,trackId);t.Title=Required(title,250,"Título");t.Artist=Optional(artist,250,"Artista");t.Album=Optional(album,250,"Álbum");t.MetadataDirty=true;return Snapshot(playlistId,t);},true);
    public void RemoveTrack(Guid playlistId,Guid trackId)=>State(d=>{Find(d,playlistId).Tracks.Remove(Find(d,playlistId,trackId));d.Jobs.RemoveAll(j=>j.TrackId==trackId);return 0;},true);
    public void ReorderTrack(Guid playlistId,Guid trackId,int newIndex)=>State(d=>{var p=Find(d,playlistId);var t=Find(d,playlistId,trackId);if(newIndex<0||newIndex>=p.Tracks.Count)throw new ArgumentOutOfRangeException(nameof(newIndex));p.Tracks.Remove(t);p.Tracks.Insert(newIndex,t);return 0;},true);
    public IReadOnlyList<Track> ImportLocalFiles(Guid playlistId,IEnumerable<string> paths)
    {
        var added=new List<Track>();foreach(string input in paths)
        {string path=Path.GetFullPath(input);if(!File.Exists(path)||!new[]{".mp3",".flac",".ogg",".wav"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new ArgumentException("Arquivo local deve ser MP3, FLAC, OGG ou WAV existente.");
            var result=State(d=>{var p=Find(d,playlistId);if(p.Tracks.Count>=500)throw new ArgumentException("Limite de faixas.");var t=new Track{Title=Path.GetFileNameWithoutExtension(path),Source=path};p.Tracks.Add(t);return Snapshot(playlistId,t);},true);added.Add(result);}
        return added;
    }
    public ImportResult ImportText(Guid playlistId,string text)
    {
        if(text.Length>1_000_000)throw new ArgumentException("Lista de texto excede limite de1MB.");
        int count=0;var warnings=new List<string>();foreach(string line in text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {try{var parts=line.Split(" - ",2);var t=AddTrack(playlistId,parts.Length==2?parts[1]:line,parts.Length==2?parts[0]:null);count++;}catch(ArgumentException e){warnings.Add(e.Message);}}
        return new(count,warnings.Count==0,null,warnings);
    }
    public ImportResult ImportCsv(Guid playlistId,string csv)
    {
        if(csv.Length>4_000_000)throw new ArgumentException("CSV excede limite de4MB.");
        using var parser=new TextFieldParser(new StringReader(csv)){HasFieldsEnclosedInQuotes=true,TrimWhiteSpace=true};parser.SetDelimiters(csv.Split('\n')[0].Contains(';')?";":",");
        if(parser.EndOfData)return new(0,true,null,[]);var headers=parser.ReadFields()!;
        int Column(params string[] names)=>Array.FindIndex(headers,h=>names.Any(n=>h.Trim().Trim('\uFEFF').Equals(n,StringComparison.OrdinalIgnoreCase)));
        int title=Column("Track Name","Title","Name","Track","Título","Faixa"),artist=Column("Artist Name(s)","Artist Name","Artist","Artists","Artista"),album=Column("Album Name","Album","Álbum"),url=Column("YouTube URL","Youtube","URL");
        if(title<0&&url<0)throw new ArgumentException("CSV requer coluna Title/Track Name ou YouTube URL.");int count=0;var warnings=new List<string>();
        while(!parser.EndOfData){var row=parser.ReadFields()!;string Cell(int i)=>i>=0&&i<row.Length?row[i]:"";try{var track=AddTrack(playlistId,string.IsNullOrWhiteSpace(Cell(url))?Cell(title):Cell(url),Cell(artist));if(!string.IsNullOrWhiteSpace(Cell(album)))UpdateTrack(playlistId,track.Id,track.Title,track.Artist,Cell(album));count++;}catch(ArgumentException ex){warnings.Add(ex.Message);}}
        return new(count,warnings.Count==0,null,warnings);
    }
    private static string Optional(string? value,int max,string field)
    {value??="";if(value.Length>max||value.Any(char.IsControl))throw new ArgumentException(field+": valor longo ou inválido.");return value.Trim();}
    private static string Required(string value,int max,string field){if(string.IsNullOrWhiteSpace(value)||value.Length>max||value.Any(char.IsControl))throw new ArgumentException(field+": valor vazio, longo ou inválido.");return value.Trim();}
    private static bool IsYouTube(Uri uri)=>string.IsNullOrEmpty(uri.UserInfo)&&uri.Scheme is "http" or "https"&&(uri.Host.Equals("youtu.be",StringComparison.OrdinalIgnoreCase)||uri.Host.Equals("youtube.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".youtube.com",StringComparison.OrdinalIgnoreCase));
    private static bool IsYouTubeUrl(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&IsYouTube(uri);
    private static bool IsPlaylistUrl(string value)=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&IsYouTube(uri)&&(uri.Query.Contains("list=",StringComparison.OrdinalIgnoreCase)||uri.AbsolutePath=="/playlist");
}
