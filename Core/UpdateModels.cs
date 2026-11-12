namespace Burnout3.PlaylistStudio.Core;

public sealed record UpdateRelease(Version Version,string Tag,string AssetName,string AssetUrl,string ChecksumsUrl,string PageUrl);
public sealed record UpdateCheckResult(Version CurrentVersion,Version? LatestVersion,bool Available,bool UpToDate,string Message,UpdateRelease? Release)
{public DateTime? CooldownUntilUtc {get;init;}}
public sealed record UpdateProgress(string Stage,double? Percent,string Message);
public sealed record StagedUpdate(string ManifestPath,string HelperExePath,Version Version,string ProgramDirectory)
{public string ManifestSha256 {get;init;}="";public string HelperSha256 {get;init;}="";}
public sealed record UpdateApplyResult(bool Success,bool RolledBack,string Message,string ProgramExePath,string BackupDirectory)
{public IReadOnlyList<string> RestartArguments {get;init;}=[];public bool RestartAfterApply {get;init;}=true;}
