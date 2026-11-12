using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Burnout3.PlaylistStudio.Core;

/// <summary>Fixed-repository portable updater. It never launches software or edits user/game data.</summary>
public sealed class PortableUpdateService
{
    public const string Repository="eep0x10/burnout3-playlist-studio";
    private const string MainExe="BurnoutPlaylistStudio.exe",HelperExe="BurnoutPlaylistStudio.UpdateWorker.exe";
    private const long MaxZip=256L*1024*1024,MaxExpanded=512L*1024*1024;
    private static readonly HttpClient DefaultHttp=new(){Timeout=TimeSpan.FromMinutes(15)};
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    private static readonly HashSet<string> RootFiles=new(StringComparer.OrdinalIgnoreCase){MainExe,"Launch-Takedawn.vbs","README.md","README.pt-BR.md","README.es.md","LICENSE","THIRD_PARTY_NOTICES.md","DOTNET-LICENSE.txt","DOTNET-ThirdPartyNotices.txt"};
    private static string UpdateRoot=>Path.Combine(Path.GetTempPath(),"BurnoutPlaylistStudio-Updates");
    private readonly string programDirectory;private readonly HttpClient http;
    internal Func<bool>? GameRunningForTests {get;set;}
    internal Action<int>? AfterReplaceForTests {get;set;}
    internal Func<string,Version?>? ExecutableVersionForTests {get;set;}
    internal TimeSpan GateTimeoutForTests {get;set;}=TimeSpan.FromSeconds(15);
    public PortableUpdateService(string?programDirectory=null,HttpClient?http=null)
    {this.programDirectory=Path.TrimEndingDirectorySeparator(Path.GetFullPath(programDirectory??AppContext.BaseDirectory));this.http=http??DefaultHttp;}
    private sealed record PlanFile(string RelativePath,string Sha256,string? OriginalSha256);
    private sealed class Plan
    {
        public int Format{get;set;}=1;public string ProgramDirectory{get;set;}="";public string Version{get;set;}="";public string Tag{get;set;}="";public string OriginalExeSha256{get;set;}="";public string ZipSha256{get;set;}="";
        public int ParentProcessId{get;set;}public DateTime ParentStartTimeUtc{get;set;}public List<PlanFile> Files{get;set;}=[];public List<string> RestartArguments{get;set;}=[];public bool RestartAfterApply{get;set;}=true;
    }
    private sealed record Cooldown(string Version,DateTime UntilUtc);
    public static IDisposable AcquireProgramUseLease(string programDirectory)
    {
        string path=GatePath(programDirectory);GuardAncestors(path);
        return new FileStream(path,FileMode.OpenOrCreate,FileAccess.Read,FileShare.Read);
    }
    private static string GatePath(string root)
    {
        string folder=Path.Combine(UpdateRoot,"status");GuardAncestors(folder);Directory.CreateDirectory(folder);GuardAncestors(folder);
        return Path.Combine(folder,RootKey(root)+".use.lock");
    }
    public static void MarkGameLaunchIntent(string programDirectory)
    {
        string path=LaunchIntentPath(programDirectory);GuardAncestors(path);SafeFiles.Write(path,Encoding.UTF8.GetBytes(DateTime.UtcNow.AddSeconds(60).ToString("O",System.Globalization.CultureInfo.InvariantCulture)),false);
    }
    private static string LaunchIntentPath(string root)=>Path.Combine(Path.GetDirectoryName(GatePath(root))!,RootKey(root)+".launch-intent.txt");
    public static bool IsGameLaunchPending(string root)
    {
        string path=LaunchIntentPath(root);GuardAncestors(path);if(!File.Exists(path))return false;
        if(!DateTime.TryParseExact(File.ReadAllText(path),"O",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out var until)||until.Kind!=DateTimeKind.Utc)return true;
        return until>DateTime.UtcNow;
    }
    internal static void ClearLaunchIntentForTests(string root){string path=LaunchIntentPath(root);GuardAncestors(path);if(File.Exists(path))File.Delete(path);}
    private static string RootKey(string root)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant())));
    private static string CooldownPath(string root)=>Path.Combine(Path.GetDirectoryName(GatePath(root))!,RootKey(root)+".cooldown.json");
    private static void MarkFailure(string root,string version)
    {try{string path=CooldownPath(root);GuardAncestors(path);SafeFiles.Write(path,JsonSerializer.SerializeToUtf8Bytes(new Cooldown(version,DateTime.UtcNow.AddHours(24)),Json),false);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion,CancellationToken cancellationToken=default,bool ignoreCooldown=false)
    {
        try
        {
            byte[] bytes=await DownloadBytes("https://api.github.com/repos/"+Repository+"/releases/latest",1024*1024,cancellationToken);
            using var document=JsonDocument.Parse(bytes);var release=document.RootElement;
            if(!release.TryGetProperty("draft",out var draft)||draft.ValueKind!=JsonValueKind.False||!release.TryGetProperty("prerelease",out var pre)||pre.ValueKind!=JsonValueKind.False)throw new InvalidDataException("A release recebida não é estável/publicada.");
            string tag=release.GetProperty("tag_name").GetString()??"";var parsed=ParseVersion(tag);
            string expected=AssetName(tag);var assets=release.GetProperty("assets").EnumerateArray().ToArray();
            string Asset(string name)
            {
                var matches=assets.Where(a=>a.TryGetProperty("name",out var n)&&n.GetString()==name).ToArray();if(matches.Length!=1)throw new InvalidDataException("Asset ausente/duplicado: "+name);
                var asset=matches[0];if(asset.TryGetProperty("state",out var state)&&state.GetString()!="uploaded")throw new InvalidDataException("Upload de atualização incompleto.");
                if(!asset.TryGetProperty("size",out var size)||!size.TryGetInt64(out long length)||length<=0||length>(name==expected?MaxZip:128*1024))throw new InvalidDataException("Tamanho de asset inválido.");
                string url=asset.GetProperty("browser_download_url").GetString()??"";ValidateAssetUrl(url,tag,name);return url;
            }
            var info=new UpdateRelease(parsed,tag,expected,Asset(expected),Asset("SHA256SUMS.txt"),"https://github.com/"+Repository+"/releases/tag/"+tag);
            bool newer=Normalize(parsed)>Normalize(currentVersion);
            if(newer&&!ignoreCooldown)
            {
                string path=CooldownPath(programDirectory);GuardAncestors(path);
                if(File.Exists(path))
                {
                    var cooldown=JsonSerializer.Deserialize<Cooldown>(File.ReadAllBytes(path));
                    if(cooldown!=null&&cooldown.Version==parsed.ToString()&&cooldown.UntilUtc>DateTime.UtcNow)return new(currentVersion,parsed,false,false,"Tentativa automática adiada após uma falha. Use Atualizações para tentar manualmente.",info){CooldownUntilUtc=cooldown.UntilUtc};
                }
            }
            return new(currentVersion,parsed,newer,!newer,newer?"Atualização estável disponível: "+parsed:"O aplicativo já está atualizado.",info);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex)when(ex is HttpRequestException or IOException or InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {return new(currentVersion,null,false,false,"Não foi possível verificar atualizações: "+ex.Message,null);}
    }
    public async Task<StagedUpdate> StageAsync(UpdateRelease release,int parentProcessId,DateTime parentStartTimeUtc,IProgress<UpdateProgress>?progress=null,CancellationToken cancellationToken=default,IReadOnlyList<string>?restartArguments=null,bool restartAfterApply=true)
    {
        string? stage=null;
        try
        {
            ValidateRelease(release);ValidateProgramRoot(programDirectory);var arguments=ValidateArguments(restartArguments??[]);
            if(parentProcessId<=0||parentStartTimeUtc.Kind!=DateTimeKind.Utc||parentStartTimeUtc>DateTime.UtcNow.AddMinutes(1))throw new InvalidDataException("Identidade do processo pai inválida.");
            string main=Within(programDirectory,MainExe);if(!File.Exists(main))throw new FileNotFoundException("Executável portátil não encontrado.",main);
            string originalHash=Hash(main);if(Normalize(ReadExecutableVersion(main))>=Normalize(release.Version))throw new InvalidDataException("O pacote não é mais novo que o programa instalado.");
            stage=Path.Combine(UpdateRoot,Guid.NewGuid().ToString("N"));GuardAncestors(stage);Directory.CreateDirectory(stage);GuardAncestors(stage);
            progress?.Report(new("checksum",null,"Obtendo checksum da release oficial…"));
            byte[] sums=await DownloadBytes(release.ChecksumsUrl,128*1024,cancellationToken);string expectedHash=ChecksumFor(Encoding.UTF8.GetString(sums),release.AssetName);
            string archive=Within(stage,"package.zip");progress?.Report(new("download",0,"Baixando pacote oficial…"));
            await DownloadFile(release.AssetUrl,archive,MaxZip,progress,cancellationToken);
            if(Hash(archive)!=expectedHash)throw new InvalidDataException("SHA256 do pacote não corresponde ao manifesto oficial; nada será aplicado.");
            progress?.Report(new("verify",null,"Checksum confirmado. Validando conteúdo do ZIP…"));
            var plan=new Plan{ProgramDirectory=programDirectory,Version=release.Version.ToString(),Tag=release.Tag,OriginalExeSha256=originalHash,ZipSha256=expectedHash,ParentProcessId=parentProcessId,ParentStartTimeUtc=parentStartTimeUtc,RestartArguments=arguments,RestartAfterApply=restartAfterApply};
            string files=Within(stage,"files");Directory.CreateDirectory(files);
            using(var zip=ZipFile.OpenRead(archive))
            {
                if(zip.Entries.Count>4096)throw new InvalidDataException("ZIP contém entradas demais.");long expanded=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();bool directory=entry.FullName.EndsWith('/');string relative=ValidateZipName(entry.FullName,directory);
                    if(!seen.Add(relative))throw new InvalidDataException("ZIP contém nomes duplicados (inclusive maiúsculas/minúsculas).");
                    int unixMode=(entry.ExternalAttributes>>16)&0xF000;if(unixMode==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("Links/reparse points não são aceitos no ZIP.");
                    if(directory){Directory.CreateDirectory(Within(files,relative));continue;}
                    expanded=checked(expanded+entry.Length);if(entry.Length<0||entry.Length>MaxZip||expanded>MaxExpanded)throw new InvalidDataException("ZIP excede limite descompactado.");
                    string output=Within(files,relative);Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    using(var input=entry.Open())using(var target=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))await CopyBounded(input,target,entry.Length,cancellationToken);
                    if(new FileInfo(output).Length!=entry.Length)throw new InvalidDataException("Tamanho extraído não confere.");
                    string destination=Within(programDirectory,relative);plan.Files.Add(new(relative,Hash(output),File.Exists(destination)?Hash(destination):null));
                }
            }
            if(!plan.Files.Any(f=>f.RelativePath==MainExe))throw new InvalidDataException("ZIP não contém o executável esperado.");
            if(Normalize(ReadExecutableVersion(Within(files,MainExe)))!=Normalize(release.Version))throw new InvalidDataException("A versão do executável ZIP diverge da tag publicada.");
            // The helper is an exact copy of the current installed program, never downloaded code.
            string helper=Within(stage,HelperExe);File.Copy(main,helper);if(Hash(helper)!=originalHash||Hash(main)!=originalHash)throw new IOException("Programa mudou durante o preparo da atualização.");
            string manifest=Within(stage,"update-plan.json");SafeFiles.Write(manifest,JsonSerializer.SerializeToUtf8Bytes(plan,Json),false);
            progress?.Report(new("staged",100,"Pacote verificado e pronto; dados do usuário preservados."));return new(manifest,helper,release.Version,programDirectory){ManifestSha256=Hash(manifest),HelperSha256=originalHash};
        }
        catch(OperationCanceledException){if(stage!=null)RemoveOwnedStage(stage);throw;}
        catch{MarkFailure(programDirectory,release.Version.ToString());if(stage!=null)RemoveOwnedStage(stage);throw;}
    }
    public Task<UpdateApplyResult> ApplyAsync(string manifestPath,CancellationToken cancellationToken=default,string?expectedManifestSha256=null)
        =>ApplyInternal(manifestPath,Environment.ProcessPath??"",null,cancellationToken,expectedManifestSha256);
    internal Task<UpdateApplyResult> ApplyForTestsAsync(string manifestPath,string workerPath,Func<int,DateTime,CancellationToken,Task>?parentWaiter=null,CancellationToken cancellationToken=default,string?expectedManifestSha256=null)
        =>ApplyInternal(manifestPath,workerPath,parentWaiter,cancellationToken,expectedManifestSha256);
    private async Task<UpdateApplyResult> ApplyInternal(string manifestPath,string workerPath,Func<int,DateTime,CancellationToken,Task>?parentWaiter,CancellationToken ct,string?expectedManifestSha256)
    {
        Plan? plan=null;string stage="",backup="";
        try
        {
            stage=ValidateStagePath(manifestPath);if(expectedManifestSha256==null||!Regex.IsMatch(expectedManifestSha256,@"^[0-9A-Fa-f]{64}$")||!Hash(manifestPath).Equals(expectedManifestSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Hash do plano de atualização inválido/alterado.");if(new FileInfo(manifestPath).Length>1024*1024)throw new InvalidDataException("Plano de atualização grande demais.");
            plan=JsonSerializer.Deserialize<Plan>(File.ReadAllBytes(manifestPath),Json)??throw new InvalidDataException("Plano inválido.");ValidatePlan(plan,stage,workerPath);
            if(GameRunning())throw new UpdateBlockedException("Feche o Takedawn antes de atualizar; nenhum arquivo foi alterado.");
            await (parentWaiter??WaitForParent)(plan.ParentProcessId,plan.ParentStartTimeUtc,ct);
            return await Task.Run(()=>ApplyFiles(plan,stage,ct),ct);
        }
        catch(Exception ex)when(ex is not OutOfMemoryException)
        {
            if(plan!=null&&ex is not OperationCanceledException&&ex is not UpdateBlockedException)MarkFailure(plan.ProgramDirectory,plan.Version);
            return new(false,false,"Atualização não aplicada: "+ex.Message,plan==null?"":Path.Combine(plan.ProgramDirectory,MainExe),backup){RestartArguments=plan?.RestartArguments??[],RestartAfterApply=plan?.RestartAfterApply??false};
        }
    }
    private UpdateApplyResult ApplyFiles(Plan plan,string stage,CancellationToken ct)
    {
        string backup=Within(stage,"backup");var changed=new List<PlanFile>();IDisposable?gate=null;
        try
        {
            var wait=Stopwatch.StartNew();while(gate==null)
            {ct.ThrowIfCancellationRequested();try{gate=new FileStream(GatePath(plan.ProgramDirectory),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}catch(IOException){if(wait.Elapsed>GateTimeoutForTests)throw new IOException("Outra instância ainda usa este programa; atualização adiada.");Thread.Sleep(100);}}
            if(GameRunning()||IsGameLaunchPending(plan.ProgramDirectory))throw new UpdateBlockedException("Takedawn está aberto ou seu início está reservado; atualização adiada sem alterações.");
            EnsureNoRunningProgram(plan.ProgramDirectory);ValidateProgramRoot(plan.ProgramDirectory);ValidateFileSet(plan,stage);
            Directory.CreateDirectory(backup);
            foreach(var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();if(file.OriginalSha256==null)continue;string original=Within(plan.ProgramDirectory,file.RelativePath),copy=Within(backup,file.RelativePath);Directory.CreateDirectory(Path.GetDirectoryName(copy)!);File.Copy(original,copy,false);if(Hash(copy)!=file.OriginalSha256)throw new IOException("Backup não conferiu: "+file.RelativePath);
            }
            foreach(var file in plan.Files)
            {
                ct.ThrowIfCancellationRequested();if(GameRunning()||IsGameLaunchPending(plan.ProgramDirectory))throw new UpdateBlockedException("Takedawn abriu ou está sendo iniciado durante aplicação.");
                string source=Within(Within(stage,"files"),file.RelativePath),target=Within(plan.ProgramDirectory,file.RelativePath);
                VerifyCurrent(target,file.OriginalSha256);if(Hash(source)!=file.Sha256)throw new IOException("Arquivo preparado mudou: "+file.RelativePath);
                WriteVerified(source,target,file.Sha256,file.OriginalSha256,()=>changed.Add(file));AfterReplaceForTests?.Invoke(changed.Count);
            }
            ValidateNewFiles(plan);try{File.Delete(CooldownPath(plan.ProgramDirectory));}catch(IOException){}
            return new(true,false,"Atualização aplicada. Dados, ferramentas, músicas e configurações preservados.",Within(plan.ProgramDirectory,MainExe),backup){RestartArguments=plan.RestartArguments,RestartAfterApply=plan.RestartAfterApply};
        }
        catch(Exception ex)when(ex is not OutOfMemoryException)
        {
            bool restored=true;
            foreach(var file in changed.AsEnumerable().Reverse())
            {
                try
                {
                    string target=Within(plan.ProgramDirectory,file.RelativePath);
                    if(File.Exists(target)&&Hash(target)!=file.Sha256)throw new IOException("Destino alterado externamente; rollback não irá sobrescrevê-lo.");
                    if(file.OriginalSha256!=null)WriteVerified(Within(backup,file.RelativePath),target,file.OriginalSha256,File.Exists(target)?file.Sha256:null);
                    else if(File.Exists(target)&&!VerifiedFileDeletion.Delete(target,file.Sha256,()=>false))throw new IOException("Arquivo novo alterado durante rollback.");
                }
                catch{restored=false;}
            }
            if(restored)try{foreach(var file in plan.Files)VerifyCurrent(Within(plan.ProgramDirectory,file.RelativePath),file.OriginalSha256);}catch{restored=false;}
            if(ex is not UpdateBlockedException&&ex is not OperationCanceledException)MarkFailure(plan.ProgramDirectory,plan.Version);
            bool rolledBack=changed.Count>0&&restored;
            return new(false,rolledBack,(rolledBack?"Falha ao aplicar; versão anterior restaurada. ":restored?"Atualização adiada sem alterar o programa. ":"Rollback incompleto; preserve o backup e restaure manualmente. ")+ex.Message,Within(plan.ProgramDirectory,MainExe),backup){RestartArguments=plan.RestartArguments,RestartAfterApply=plan.RestartAfterApply};
        }
        finally{gate?.Dispose();}
    }
    private static void WriteVerified(string source,string target,string hash,string? expectedCurrent,Action? committed=null)
    {
        GuardAncestors(source);GuardAncestors(target);if(Hash(source)!=hash)throw new IOException("Fonte de atualização/rollback modificada.");Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp=target+".update-"+Guid.NewGuid().ToString("N")+".tmp",captured=target+".preupdate-"+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            File.Copy(source,temp);if(Hash(temp)!=hash)throw new IOException("Cópia não conferiu.");GuardAncestors(target);VerifyCurrent(target,expectedCurrent);
            if(expectedCurrent==null)File.Move(temp,target); // Atomic fail-if-exists: never overwrite a concurrently created user file.
            else
            {
                File.Replace(temp,target,captured);committed?.Invoke();committed=null;
                // ReplaceFile captured the actual replaced bytes atomically. Restore a concurrent external change.
                if(Hash(captured)!=expectedCurrent)
                {
                    if(Hash(target)==hash)File.Replace(captured,target,null);
                    throw new IOException("Destino mudou durante a substituição; bytes externos foram preservados.");
                }
            }
            committed?.Invoke();if(Hash(target)!=hash)throw new IOException("Gravação final não conferiu.");
        }
        finally
        {
            if(File.Exists(temp))File.Delete(temp);
            if(File.Exists(captured)&&expectedCurrent!=null&&Hash(captured)==expectedCurrent)VerifiedFileDeletion.Delete(captured,expectedCurrent,()=>false);
        }
    }
    private static async Task WaitForParent(int pid,DateTime start,CancellationToken ct)
    {
        if(pid==Environment.ProcessId)throw new InvalidDataException("Atualizador não pode esperar pelo próprio processo.");
        Process process;try{process=Process.GetProcessById(pid);}catch(ArgumentException){return;}
        using(process)
        {
            if(process.StartTime.ToUniversalTime()!=start)throw new InvalidDataException("PID do aplicativo foi reutilizado; atualização cancelada.");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(60));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new TimeoutException("O aplicativo não encerrou em60segundos.");}
        }
    }
    private bool GameRunning()
    {if(GameRunningForTests!=null)return GameRunningForTests();var processes=Process.GetProcessesByName("Takedawn");try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}}
    private static void EnsureNoRunningProgram(string root)
    {
        foreach(var process in Process.GetProcessesByName("BurnoutPlaylistStudio"))
        {using(process){if(process.Id==Environment.ProcessId)continue;try{string?file=process.MainModule?.FileName;if(file==null||Path.GetFullPath(file).Equals(Path.Combine(root,MainExe),StringComparison.OrdinalIgnoreCase))throw new IOException("Outra instância do programa ainda está em execução.");}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception ex){throw new IOException("Não foi possível verificar outra instância do programa.",ex);}}}
    }
    private void ValidatePlan(Plan plan,string stage,string workerPath)
    {
        if(plan.Format!=1||plan.Files.Count==0||plan.Files.Count>4096||plan.ParentProcessId<=0||plan.ParentStartTimeUtc.Kind!=DateTimeKind.Utc)throw new InvalidDataException("Formato/identidade do plano inválidos.");
        ValidateProgramRoot(plan.ProgramDirectory);if(ParseVersion(plan.Tag)!=Version.Parse(plan.Version))throw new InvalidDataException("Versão do plano divergente.");ValidateArguments(plan.RestartArguments);
        string expectedWorker=Within(stage,HelperExe);if(!Path.GetFullPath(workerPath).Equals(expectedWorker,StringComparison.OrdinalIgnoreCase)||Hash(expectedWorker)!=plan.OriginalExeSha256)throw new InvalidDataException("Atualizador auxiliar não corresponde ao programa original.");
        if(Hash(Within(plan.ProgramDirectory,MainExe))!=plan.OriginalExeSha256)throw new InvalidDataException("Programa alvo mudou ou não pertence a este pacote portátil.");
        ValidateFileSet(plan,stage);
    }
    private void ValidateFileSet(Plan plan,string stage)
    {
        ValidateArchive(plan,stage);if(Normalize(ReadExecutableVersion(Within(Within(stage,"files"),MainExe)))!=Normalize(Version.Parse(plan.Version)))throw new InvalidDataException("Versão do executável preparado diverge da release.");
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var file in plan.Files)
        {
            string relative=ValidateZipName(file.RelativePath,false);if(relative!=file.RelativePath||!seen.Add(relative)||!Regex.IsMatch(file.Sha256,@"^[0-9A-F]{64}$")||(file.OriginalSha256!=null&&!Regex.IsMatch(file.OriginalSha256,@"^[0-9A-F]{64}$")))throw new InvalidDataException("Arquivos/hash do plano inválidos.");
            string source=Within(Within(stage,"files"),relative);if(Hash(source)!=file.Sha256)throw new InvalidDataException("Conteúdo preparado foi alterado.");VerifyCurrent(Within(plan.ProgramDirectory,relative),file.OriginalSha256);
        }
        if(!seen.Contains(MainExe))throw new InvalidDataException("Executável principal ausente no plano.");
    }
    private Version ReadExecutableVersion(string path)
    {
        if(ExecutableVersionForTests!=null)return ExecutableVersionForTests(path)??throw new InvalidDataException("Executável sem versão válida.");
        string? value=FileVersionInfo.GetVersionInfo(path).FileVersion;var match=Regex.Match(value??"",@"^\d+\.\d+\.\d+(?:\.\d+)?");
        return match.Success&&Version.TryParse(match.Value,out var version)?version:throw new InvalidDataException("Executável sem metadados de versão confiáveis.");
    }
    private static void ValidateArchive(Plan plan,string stage)
    {
        string archive=Within(stage,"package.zip");if(!Regex.IsMatch(plan.ZipSha256,@"^[0-9A-F]{64}$")||Hash(archive)!=plan.ZipSha256)throw new InvalidDataException("Pacote ZIP preparado foi alterado.");
        using var zip=ZipFile.OpenRead(archive);var expected=plan.Files.ToDictionary(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int count=0;long length=0;
        foreach(var entry in zip.Entries)
        {
            string relative=ValidateZipName(entry.FullName,entry.FullName.EndsWith('/'));if(!seen.Add(relative)||((entry.ExternalAttributes>>16)&0xF000)==0xA000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException("ZIP preparado contém links/duplicatas.");
            if(entry.FullName.EndsWith('/'))continue;length=checked(length+entry.Length);if(length>MaxExpanded||entry.Length>MaxZip||!expected.TryGetValue(relative,out var file))throw new InvalidDataException("Conteúdo ZIP não corresponde ao plano.");
            using var input=entry.Open();if(Convert.ToHexString(SHA256.HashData(input))!=file.Sha256)throw new InvalidDataException("Bytes ZIP divergem do conteúdo aprovado.");count++;
        }
        if(count!=plan.Files.Count)throw new InvalidDataException("Conjunto de arquivos ZIP/plano divergente.");
    }
    private static void ValidateNewFiles(Plan plan){foreach(var file in plan.Files)if(Hash(Within(plan.ProgramDirectory,file.RelativePath))!=file.Sha256)throw new IOException("Verificação pós-atualização falhou.");}
    private static void VerifyCurrent(string path,string?expected){if(expected==null){if(File.Exists(path)||Directory.Exists(path))throw new IOException("Destino novo passou a existir: "+Path.GetFileName(path));}else if(!File.Exists(path)||Hash(path)!=expected)throw new IOException("Arquivo instalado mudou: "+Path.GetFileName(path));}
    private static string ValidateStagePath(string manifest)
    {
        string full=Path.GetFullPath(manifest),stage=Path.GetDirectoryName(full)??"";string?parent=Path.GetDirectoryName(stage);
        if(Path.GetFileName(full)!="update-plan.json"||!string.Equals(parent,Path.GetFullPath(UpdateRoot),StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(Path.GetFileName(stage),@"^[0-9a-f]{32}$"))throw new InvalidDataException("Plano deve estar na pasta temporária própria do atualizador.");GuardAncestors(full);return stage;
    }
    private static void ValidateProgramRoot(string root)
    {
        root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));string drive=Path.TrimEndingDirectorySeparator(Path.GetPathRoot(root)??"");
        if(root==drive||root.Equals(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),StringComparison.OrdinalIgnoreCase)||root.Equals(Path.TrimEndingDirectorySeparator(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Raiz de programa ampla demais.");
        string windows=Environment.GetFolderPath(Environment.SpecialFolder.Windows);if(windows.Length>0&&(root.Equals(windows,StringComparison.OrdinalIgnoreCase)||root.StartsWith(windows+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Pasta Windows não pode ser atualizada.");
        if(!Directory.Exists(root))throw new DirectoryNotFoundException("Pasta portátil não encontrada.");GuardAncestors(root);
    }
    private static string Within(string root,string relative){string full=SafeFiles.Within(root,Path.Combine(root,relative));GuardAncestors(full);return full;}
    private static void GuardAncestors(string path)
    {for(string?item=Path.GetFullPath(path);item!=null;item=Path.GetDirectoryName(item))if((File.Exists(item)||Directory.Exists(item))&&(File.GetAttributes(item)&FileAttributes.ReparsePoint)!=0)throw new IOException("Atualização não aceita links/junções em seus caminhos.");}
    private static string ValidateZipName(string value,bool directory)
    {
        if(value.Length==0||value.Length>240||value.Contains('\\')||value.Contains(':')||value.StartsWith('/')||value.Any(char.IsControl))throw new InvalidDataException("Caminho ZIP inválido.");string name=directory?value.TrimEnd('/'):value;var parts=name.Split('/');
        if(parts.Any(p=>p.Length==0||p is "." or ".."||p.EndsWith(' ')||p.EndsWith('.')||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(p.Split('.')[0],@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase)))throw new InvalidDataException("Nome ZIP inválido.");
        if(directory){if(!parts[0].Equals("docs",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Diretório fora da lista permitida.");return "docs"+(parts.Length>1?"/"+string.Join('/',parts.Skip(1)):"");}
        if(parts.Length==1&&RootFiles.TryGetValue(name,out string?canonical))return canonical;
        if(parts.Length>1&&parts[0].Equals("docs",StringComparison.OrdinalIgnoreCase)&&new[]{".md",".png"}.Contains(Path.GetExtension(name).ToLowerInvariant()))return "docs/"+string.Join('/',parts.Skip(1));
        throw new InvalidDataException("ZIP contém arquivo não permitido: "+name);
    }
    private static List<string> ValidateArguments(IReadOnlyList<string> args)
    {
        if(args.Count>5)throw new InvalidDataException("Argumentos de reinício inválidos.");var result=new List<string>();var seen=new HashSet<string>(StringComparer.Ordinal);
        for(int i=0;i<args.Count;i++)
        {
            string key=args[i];if(!seen.Add(key)||key is not("--data" or "--config" or "--minimized"))throw new InvalidDataException("Opção de reinício não permitida.");result.Add(key);
            if(key!="--minimized"){if(++i>=args.Count||!Path.IsPathFullyQualified(args[i])||args[i].Any(char.IsControl))throw new InvalidDataException("Caminho de reinício deve ser absoluto.");result.Add(Path.GetFullPath(args[i]));}
        }
        return result;
    }
    private static Version Normalize(Version value)=>new(value.Major,value.Minor,Math.Max(value.Build,0),Math.Max(value.Revision,0));
    private static Version ParseVersion(string tag){if(!Regex.IsMatch(tag,@"^v?\d+\.\d+\.\d+(?:\.\d+)?$",RegexOptions.CultureInvariant)||!Version.TryParse(tag.TrimStart('v'),out var version))throw new InvalidDataException("Tag de versão estável inválida.");return version;}
    private static string AssetName(string tag){ParseVersion(tag);return "BurnoutPlaylistStudio-"+tag.TrimStart('v')+"-win-x64.zip";}
    private static void ValidateRelease(UpdateRelease release){if(ParseVersion(release.Tag)!=release.Version||release.AssetName!=AssetName(release.Tag))throw new InvalidDataException("Release/asset divergentes.");ValidateAssetUrl(release.AssetUrl,release.Tag,release.AssetName);ValidateAssetUrl(release.ChecksumsUrl,release.Tag,"SHA256SUMS.txt");if(release.PageUrl!="https://github.com/"+Repository+"/releases/tag/"+release.Tag)throw new InvalidDataException("Página de release não oficial.");}
    private static void ValidateAssetUrl(string url,string tag,string name){string expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+name;if(url!=expected)throw new InvalidDataException("URL de atualização fora do repositório fixo.");}
    private static string ChecksumFor(string text,string asset)
    {var matches=text.Split('\n').Select(line=>Regex.Match(line.TrimEnd('\r'),@"^([0-9a-fA-F]{64})\s+\*?(.+)$")).Where(m=>m.Success&&m.Groups[2].Value==asset).ToArray();if(matches.Length!=1)throw new InvalidDataException("Checksum exato do pacote ausente/duplicado.");return matches[0].Groups[1].Value.ToUpperInvariant();}
    private static string Hash(string path){GuardAncestors(path);using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);return Convert.ToHexString(SHA256.HashData(file));}
    private async Task<byte[]> DownloadBytes(string url,long limit,CancellationToken ct){using var stream=new MemoryStream();await DownloadTo(url,stream,limit,null,ct);return stream.ToArray();}
    private async Task DownloadFile(string url,string output,long limit,IProgress<UpdateProgress>?progress,CancellationToken ct){using var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None);await DownloadTo(url,stream,limit,progress,ct);stream.Flush(true);}
    private async Task DownloadTo(string url,Stream target,long limit,IProgress<UpdateProgress>?progress,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.UserAgent.ParseAdd("BurnoutPlaylistStudio-Updater/1.3 (+https://github.com/"+Repository+")");request.Headers.Accept.ParseAdd(url.Contains("api.github.com")?"application/vnd.github+json":"application/octet-stream");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)throw new HttpRequestException("GitHub limitou a consulta (HTTP"+(int)response.StatusCode+"); tente mais tarde.",null,response.StatusCode);
        response.EnsureSuccessStatusCode();var final=response.RequestMessage?.RequestUri;if(final!=null&&(final.Scheme!="https"||!new[]{"api.github.com","github.com","release-assets.githubusercontent.com","objects.githubusercontent.com"}.Contains(final.Host)))throw new InvalidDataException("Redirecionamento de download não permitido.");
        long?total=response.Content.Headers.ContentLength;if(total>limit)throw new IOException("Download excede limite permitido.");await using var input=await response.Content.ReadAsStreamAsync(ct);var buffer=new byte[65536];long count=0;int n;
        while((n=await input.ReadAsync(buffer,ct))>0){count+=n;if(count>limit)throw new IOException("Download excede limite permitido.");await target.WriteAsync(buffer.AsMemory(0,n),ct);progress?.Report(new("download",total>0?count*100d/total:null,"Baixando pacote verificado…"));}
    }
    private static async Task CopyBounded(Stream source,Stream target,long limit,CancellationToken ct){var buffer=new byte[65536];long count=0;int n;while((n=await source.ReadAsync(buffer,ct))>0){count+=n;if(count>limit)throw new InvalidDataException("Entrada ZIP excede tamanho anunciado.");await target.WriteAsync(buffer.AsMemory(0,n),ct);}}
    private static void RemoveOwnedStage(string stage)
    {
        try
        {
            ValidateStagePath(Path.Combine(stage,"update-plan.json"));
            void Remove(string directory){GuardAncestors(directory);foreach(string child in Directory.EnumerateFileSystemEntries(directory)){GuardAncestors(child);if(Directory.Exists(child))Remove(child);else File.Delete(child);}Directory.Delete(directory,false);}
            Remove(stage);
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
    }
    private sealed class UpdateBlockedException(string message):IOException(message);
}

