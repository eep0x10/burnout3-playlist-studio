namespace Burnout3.PlaylistStudio.Core;



public enum PlaylistSourceKind { None, YouTube, Spotify }
public enum TrackStatus { Added, Resolving, Ready, Downloading, Downloaded, Failed, Cancelled }

public enum LyricsStatus { Pending, Synced, Plain, Instrumental, NotFound, Failed }

public enum JobStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed record Track

{

    public Guid Id { get; init; }=Guid.NewGuid();

    public string? RemoteId { get; set; }
    public Dictionary<string,string> ManagedHashes {get;set;}=[];
    public string Title { get; set; }="";

    public string Artist { get; set; }="";

    public string Album { get; set; }="";

    public string Source { get; set; }="";

    public string? YouTubeUrl { get; set; }

    public double? DurationSeconds { get; set; }

    public string? ArtworkUrl { get; set; }

    public TrackStatus Status { get; set; }=TrackStatus.Added;

    public LyricsStatus LyricsStatus { get; set; }=LyricsStatus.Pending;

    public string? Error { get; set; }

    public string? LyricsError { get; set; }

    public string? InstalledPath { get; set; }

    public bool MetadataOverride {get;set;}
    public bool MetadataDirty { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string? LocalPath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string? LyricsPath { get; set; }

}

public sealed record Playlist

{

    public Guid Id { get; init; }=Guid.NewGuid();

    public string Name { get; set; }="New playlist";

    public List<Track> Tracks { get; set; }=[];

    public List<Guid> SourceAdoptionCandidates {get;set;}=[];
    public bool AdoptExistingOnNextSync {get;set;}
    public string? SourceUrl {get;set;}
    public PlaylistSourceKind SourceKind {get;set;}
    public DateTime? LastSyncUtc {get;set;}
    public bool? LastSyncComplete {get;set;}
    public string? SyncWarning {get;set;}
    public bool PendingActivation {get;set;}
    public DateTime CreatedUtc { get; init; }=DateTime.UtcNow;

}

public sealed record DownloadJob

{

    public Guid Id { get; init; }=Guid.NewGuid();

    public Guid PlaylistId { get; init; }

    public Guid TrackId { get; init; }

    public JobStatus Status { get; set; }=JobStatus.Queued;

    public int Attempts { get; set; }

    public string? Error { get; set; }

public DateTime CreatedUtc { get; init; }=DateTime.UtcNow;

    public DateTime? FinishedUtc { get; set; }

}

public sealed record LibrarySnapshot(List<Playlist> Playlists,List<DownloadJob> Jobs,string TakedawnRoot)
{ public Guid? ActivePlaylistId {get;init;} }

public sealed record YouTubeCandidate(string Id,string Title,string Artist,string Url,double?DurationSeconds,string?Thumbnail=null);

public sealed record OperationProgress(Guid PlaylistId,Guid TrackId,string Stage,double?Percent,string Message);

public sealed record OperationResult(int Succeeded,int Failed,int Cancelled,IReadOnlyList<string> Warnings);

public sealed record ImportResult(int Added,bool Complete,string?PlaylistName,IReadOnlyList<string> Warnings)

{ public string Message=>$"{Added} faixa(s) importada(s). "+(Complete?"Importação completa.":"Integralidade não confirmada. ")+string.Join(" ",Warnings); }

public sealed record ToolStatus(bool Ready,string YtDlpPath,string FfmpegPath,string?DenoPath,IReadOnlyList<string> Missing)
{ public string FfprobePath=>Path.Combine(Path.GetDirectoryName(FfmpegPath)!,"ffprobe.exe"); }

public sealed record InstallResult(string RootDirectory,string PlaylistDirectory,int Copied,int Skipped,IReadOnlyList<string> Warnings);

public sealed record ConfigResult(string ConfigPath,string?BackupPath,string MusicRoot);

public sealed record ToolResult(int ExitCode,string StandardOutput,string StandardError);

public interface IToolRunner

{

    Task<ToolResult> RunAsync(string executable,IReadOnlyList<string> arguments,Action<string>? output=null,CancellationToken cancellationToken=default);

}


public sealed record TakedawnCompatibility(string? ExecutablePath,Version? InstalledVersion,bool AutomaticSupported,string Message);
public sealed record ActivationResult(Guid PlaylistId,string PlaylistName,string MusicRoot,Version? InstalledVersion,bool AutomaticSupported,string ConfigPath,string?BackupPath,string Message);
public sealed record ActivePlaylistStatus(Guid?PlaylistId,string?PlaylistName,string?MusicRoot,bool ConfigMatches,bool FilesReady,bool AutomaticSupported,bool ManualSelectionRequired,int TrackCount,Version?InstalledVersion,string Message);

public sealed record SyncResult(int Added,int Removed,bool Complete,int Succeeded,int Failed,bool AppliedToGame,bool PendingActivation,IReadOnlyList<string> Warnings)
{public string Message=>$"Atualização: {Added} adicionadas, {Removed} removidas, {Succeeded} preparadas, {Failed} falhas. "+(Complete?"Lista completa. ":"Integralidade remota não confirmada; remoções remotas suspensas. ")+(PendingActivation?"Aplicação no jogo pendente. ":AppliedToGame?"Playlist ativa atualizada. ":"")+string.Join(" ",Warnings);}
