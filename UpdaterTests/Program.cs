using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Burnout3.PlaylistStudio.Core;

string root=Path.Combine(Path.GetTempPath(),"PlaylistStudio-UpdaterTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int passed=0;
void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);passed++;}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var parent=System.Diagnostics.Process.GetCurrentProcess();var ownParentStart=parent.StartTime.ToUniversalTime();
var stages=new List<StagedUpdate>();
PortableUpdateService Service(string folder,FixtureHttp fixture)
{
    Directory.CreateDirectory(folder);if(!File.Exists(Path.Combine(folder,"BurnoutPlaylistStudio.exe")))File.WriteAllText(Path.Combine(folder,"BurnoutPlaylistStudio.exe"),"OLD OWN STUB, NEVER EXECUTED");
    return new(folder,new HttpClient(fixture)){GameRunningForTests=()=>false,GateTimeoutForTests=TimeSpan.FromMilliseconds(150),ExecutableVersionForTests=path=>path.Contains(Path.DirectorySeparatorChar+"files"+Path.DirectorySeparatorChar)?new Version(1,4,0):new Version(1,3,0)};
}
async Task<StagedUpdate> Stage(PortableUpdateService service,FixtureHttp http,IReadOnlyList<string>?args=null,bool restart=true)
{
    var result=await service.CheckAsync(new Version(1,3,0),ignoreCooldown:true);Check(result.Available&&result.Release!=null,"Fixture stable release is newer and comes from fixed repository");
    var staged=await service.StageAsync(result.Release!,Environment.ProcessId,ownParentStart,restartArguments:args,restartAfterApply:restart);stages.Add(staged);return staged;
}
Task<UpdateApplyResult> Apply(PortableUpdateService service,StagedUpdate staged,Func<int,DateTime,CancellationToken,Task>?waiter=null)
    =>service.ApplyForTestsAsync(staged.ManifestPath,staged.HelperExePath,waiter??((_,_,_)=>Task.CompletedTask),expectedManifestSha256:staged.ManifestSha256);
var fixture=new FixtureHttp();string program=Path.Combine(root,"portable");var updater=Service(program,fixture);
var normal=await updater.CheckAsync(new Version(1,3,0));Check(normal.Available&&!normal.UpToDate&&normal.LatestVersion==new Version(1,4,0),"Semantic newer release check succeeds");
var equal=await updater.CheckAsync(new Version(1,4,0,0));Check(!equal.Available&&equal.UpToDate,"Three/four component equivalent versions never cause an update loop");
fixture.Prerelease=true;Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Prerelease latest response is rejected");fixture.Prerelease=false;fixture.Draft=true;Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Draft release is rejected");fixture.Draft=false;
fixture.Tag="v1.4.0-beta";Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Non-numeric release tag is rejected");fixture.Tag="v1.4.0";
fixture.Forbidden=true;var limited=await updater.CheckAsync(new Version(1,3,0));Check(!limited.Available&&!limited.UpToDate&&limited.Message.Contains("403"),"GitHub rate403 reports unavailable rather than claiming current version");fixture.Forbidden=false;
fixture.WrongRepository=true;Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Asset URL outside pinned owner/repository is rejected");fixture.WrongRepository=false;
fixture.DuplicateAsset=true;Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Duplicate matching release assets are rejected");fixture.DuplicateAsset=false;
fixture.HugeDeclared=true;Check(!(await updater.CheckAsync(new Version(1,3,0))).Available,"Oversized declared archive is rejected before download");fixture.HugeDeclared=false;
using(var cts=new CancellationTokenSource()){cts.Cancel();bool cancelled=false;try{await updater.CheckAsync(new Version(1,3,0),cts.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"Cancelled update check propagates without applying anything");}
string main=Path.Combine(program,"BurnoutPlaylistStudio.exe"),original=Hash(main);Directory.CreateDirectory(Path.Combine(program,"data"));Directory.CreateDirectory(Path.Combine(program,"tools"));File.WriteAllText(Path.Combine(program,"data","library.json"),"PRIVATE SENTINEL");File.WriteAllText(Path.Combine(program,"tools","ffmpeg.exe"),"TOOLS SENTINEL");File.WriteAllText(Path.Combine(program,"unknown-user.wav"),"USER AUDIO");File.WriteAllText(Path.Combine(program,"README.md"),"OLD README");string external=Path.Combine(root,"external-game-settings.toml");File.WriteAllText(external,"PRIVATE CONFIG");
fixture.BadChecksum=true;bool badHash=false;try{await Stage(updater,fixture);}catch(InvalidDataException){badHash=true;}Check(badHash&&Hash(main)==original,"Wrong package SHA aborts before extraction/application");fixture.BadChecksum=false;
var cooldown=await updater.CheckAsync(new Version(1,3,0));Check(!cooldown.Available&&cooldown.CooldownUntilUtc>DateTime.UtcNow,"Failed version is cooled down across service checks");Check((await updater.CheckAsync(new Version(1,3,0),ignoreCooldown:true)).Available,"Manual update check can explicitly bypass automatic cooldown");
foreach(string badName in new[]{"../escape.txt","/absolute.md","C:/escape.md","docs/../escape.md","docs\\escape.md","data/library.json","tools/yt-dlp.exe","unknown.exe","docs/payload.dll","docs/CON.md"})
{
    fixture.Zip=FixtureHttp.ZipOf(("BurnoutPlaylistStudio.exe","NEW OWN STUB"),(badName,"MALICIOUS"));bool rejected=false;try{await Stage(updater,fixture);}catch(InvalidDataException){rejected=true;}Check(rejected&&Hash(main)==original,"ZIP rejects forbidden path: "+badName);
}
fixture.Zip=FixtureHttp.ZipOf(("BurnoutPlaylistStudio.exe","NEW OWN STUB"),("README.md","ONE"),("readme.MD","TWO"));bool duplicate=false;try{await Stage(updater,fixture);}catch(InvalidDataException){duplicate=true;}Check(duplicate,"ZIP case-insensitive duplicate paths are rejected");
fixture.Zip=FixtureHttp.ZipOf(("BurnoutPlaylistStudio.exe","NEW OWN STUB"),("docs/link.md","TARGET"));using(var ms=new MemoryStream(fixture.Zip)){using(var zip=new ZipArchive(ms,ZipArchiveMode.Update,true)){zip.GetEntry("docs/link.md")!.ExternalAttributes=unchecked((int)0xA1FF0000);}fixture.Zip=ms.ToArray();}bool link=false;try{await Stage(updater,fixture);}catch(InvalidDataException){link=true;}Check(link,"ZIP symlink entry is rejected");
fixture.ResetZip();updater.ExecutableVersionForTests=path=>new Version(1,3,0);bool staleExe=false;try{await Stage(updater,fixture);}catch(InvalidDataException){staleExe=true;}Check(staleExe&&Hash(main)==original,"ZIP executable version must match release tag even with a correct archive checksum");updater.ExecutableVersionForTests=path=>path.Contains(Path.DirectorySeparatorChar+"files"+Path.DirectorySeparatorChar)?new Version(1,4,0):new Version(1,3,0);
var staged=await Stage(updater,fixture,new[]{"--data",Path.Combine(program,"data"),"--config",external,"--minimized"},false);
Check(staged.ManifestSha256==Hash(staged.ManifestPath)&&staged.HelperSha256==original&&Hash(staged.HelperExePath)==original,"Prepared manifest is pinned and helper is copied original program, never downloaded code");
bool unsafeArgs=false;try{await updater.StageAsync(normal.Release!,Environment.ProcessId,ownParentStart,restartArguments:new[]{"--sync-before-game","evil.exe"});}catch(InvalidDataException){unsafeArgs=true;}Check(unsafeArgs,"Worker restart arguments cannot invoke game/prelaunch/arbitrary modes");
var noPin=await updater.ApplyForTestsAsync(staged.ManifestPath,staged.HelperExePath,(_,_,_)=>Task.CompletedTask);Check(!noPin.Success&&!noPin.RolledBack&&Hash(main)==original,"Applying without the trusted manifest hash is rejected");
var parentFailure=await Apply(updater,staged,(_,_,_)=>throw new TimeoutException("synthetic parent did not exit"));Check(!parentFailure.Success&&!parentFailure.RolledBack&&Hash(main)==original,"Parent timeout aborts without killing processes or changing files");
using(var read1=PortableUpdateService.AcquireProgramUseLease(program))using(var read2=PortableUpdateService.AcquireProgramUseLease(program))
{var blocked=await Apply(updater,staged);Check(!blocked.Success&&Hash(main)==original,"Multiple normal shared leases coexist and block replacement until all instances exit");}
using(var launchLease=PortableUpdateService.AcquireProgramUseLease(program))PortableUpdateService.MarkGameLaunchIntent(program);
Check((await updater.CheckAsync(new Version(1,3,0),ignoreCooldown:true)).Available&&PortableUpdateService.IsGameLaunchPending(program),"Launch intent keeps metadata/staging available while explicitly blocking apply");
var launchBlocked=await Apply(updater,staged);Check(!launchBlocked.Success&&Hash(main)==original,"Prelaunch success intent blocks replacement across the launcher-to-game startup gap");PortableUpdateService.ClearLaunchIntentForTests(program);
updater.GameRunningForTests=()=>true;var gameBlocked=await Apply(updater,staged);Check(!gameBlocked.Success&&Hash(main)==original,"Game-open handoff is refused before any program writes");updater.GameRunningForTests=()=>false;
string sourceExe=Path.Combine(Path.GetDirectoryName(staged.ManifestPath)!,"files","BurnoutPlaylistStudio.exe");string pristine=File.ReadAllText(sourceExe);File.AppendAllText(sourceExe,"CHANGED");var tampered=await Apply(updater,staged);Check(!tampered.Success&&Hash(main)==original,"Changed prepared bytes are revalidated and rejected before writes");File.WriteAllText(sourceExe,pristine);
string planText=File.ReadAllText(staged.ManifestPath);var planNode=JsonNode.Parse(planText)!;planNode["ProgramDirectory"]=Path.GetPathRoot(program);File.WriteAllText(staged.ManifestPath,planNode.ToJsonString());var badPlan=await Apply(updater,staged);Check(!badPlan.Success&&Hash(main)==original,"Plan mutation/root substitution fails the trusted hash pin");File.WriteAllText(staged.ManifestPath,planText);
updater.AfterReplaceForTests=count=>{if(count==2)throw new IOException("synthetic mid-transaction failure");};var rolledBack=await Apply(updater,staged);Check(!rolledBack.Success&&rolledBack.RolledBack&&Hash(main)==original&&File.ReadAllText(Path.Combine(program,"README.md"))=="OLD README","Mid-transaction failure restores exact backed-up originals");Check(File.Exists(Path.Combine(rolledBack.BackupDirectory,"BurnoutPlaylistStudio.exe"))&&Hash(Path.Combine(rolledBack.BackupDirectory,"BurnoutPlaylistStudio.exe"))==original,"Rollback backup hash matches original program");
updater.AfterReplaceForTests=null;var lockedStage=await Stage(updater,fixture);
using(var lockedReadme=new FileStream(Path.Combine(program,"README.md"),FileMode.Open,FileAccess.Read,FileShare.Read))
{var lockedFailure=await Apply(updater,lockedStage);Check(!lockedFailure.Success&&lockedFailure.RolledBack&&Hash(main)==original,"A genuinely locked second destination triggers rollback of the already replaced executable");}
var newFileStage=await Stage(updater,fixture);updater.AfterReplaceForTests=count=>{if(count==3)throw new IOException("failure after new guide created");};var newFileRollback=await Apply(updater,newFileStage);Check(newFileRollback.RolledBack&&!File.Exists(Path.Combine(program,"docs","NEW.md"))&&Hash(main)==original,"Rollback removes only newly installed approved file while restoring original program");
var partialStage=await Stage(updater,fixture);updater.AfterReplaceForTests=count=>{if(count==2){File.WriteAllText(main,"EXTERNAL USER CHANGE");throw new IOException("simulated external concurrent change");}};var partialRollback=await Apply(updater,partialStage);Check(!partialRollback.Success&&!partialRollback.RolledBack&&File.ReadAllText(main)=="EXTERNAL USER CHANGE","Partial rollback never claims restart-safe success or overwrites externally changed bytes");File.WriteAllText(main,"OLD OWN STUB, NEVER EXECUTED");updater.AfterReplaceForTests=null;
var cancelledStage=await Stage(updater,fixture);using(var cancelledApply=new CancellationTokenSource()){cancelledApply.Cancel();var cancelResult=await updater.ApplyForTestsAsync(cancelledStage.ManifestPath,cancelledStage.HelperExePath,(_,_,ct)=>Task.Delay(100,ct),cancelledApply.Token,cancelledStage.ManifestSha256);Check(!cancelResult.Success&&Hash(main)==original,"Cancelled apply leaves installed program unchanged and launches nothing");}
fixture.CancelArchive=true;bool cancelledDownload=false;try{await Stage(updater,fixture);}catch(OperationCanceledException){cancelledDownload=true;}Check(cancelledDownload&&Hash(main)==original,"Cancelled package download cannot produce an applied/unverified program");fixture.CancelArchive=false;
// Restaging gives a new empty transaction directory; an incomplete prior attempt is never reused.
updater.AfterReplaceForTests=null;var finalStage=await Stage(updater,fixture,new[]{"--data",Path.Combine(program,"data"),"--config",external,"--minimized"},false);var applied=await Apply(updater,finalStage);
Check(applied.Success&&!applied.RolledBack&&File.ReadAllText(main)=="NEW OWN STUB"&&File.ReadAllText(Path.Combine(program,"README.md"))=="NEW README","Verified sandbox update replaces only approved program files");
Check(File.ReadAllText(Path.Combine(program,"data","library.json"))=="PRIVATE SENTINEL"&&File.ReadAllText(Path.Combine(program,"tools","ffmpeg.exe"))=="TOOLS SENTINEL"&&File.ReadAllText(Path.Combine(program,"unknown-user.wav"))=="USER AUDIO"&&File.ReadAllText(external)=="PRIVATE CONFIG","Entire data/tools, unknown local music and external game configuration remain byte-identical");
Check(!applied.RestartAfterApply&&applied.RestartArguments.SequenceEqual(new[]{"--data",Path.Combine(program,"data"),"--config",external,"--minimized"}),"Validated sandbox restart arguments and companion no-restart intent survive handoff");
Console.WriteLine($"SUCCESS {passed} updater checks. Only HTTP fixtures and inert sandbox bytes were used; no program/game/helper was executed. Fixtures: {root}");

sealed class FixtureHttp:HttpMessageHandler
{
    public byte[] Zip {get;set;}=[];public bool BadChecksum,Prerelease,Draft,Forbidden,WrongRepository,DuplicateAsset,HugeDeclared,CancelArchive;public string Tag="v1.4.0";
    public FixtureHttp()=>ResetZip();
    public void ResetZip()=>Zip=ZipOf(("BurnoutPlaylistStudio.exe","NEW OWN STUB"),("README.md","NEW README"),("docs/NEW.md","NEW GUIDE"));
    public static byte[] ZipOf(params (string Name,string Data)[]files){using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true))foreach(var file in files){var entry=zip.CreateEntry(file.Name);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(file.Data);}return memory.ToArray();}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(Forbidden)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        string url=request.RequestUri!.ToString(),name="BurnoutPlaylistStudio-"+Tag.TrimStart('v')+"-win-x64.zip",prefix="https://github.com/"+(WrongRepository?"evil/wrong":PortableUpdateService.Repository)+"/releases/download/"+Tag+"/";HttpContent content;
        if(url.Contains("api.github.com"))
        {var assets=new List<object>{new{name,size=HugeDeclared?999999999L:Zip.Length,browser_download_url=prefix+name,state="uploaded"},new{name="SHA256SUMS.txt",size=200,browser_download_url=prefix+"SHA256SUMS.txt",state="uploaded"}};if(DuplicateAsset)assets.Add(assets[0]);content=new StringContent(JsonSerializer.Serialize(new{tag_name=Tag,draft=Draft,prerelease=Prerelease,assets}),Encoding.UTF8,"application/json");}
        else if(url.EndsWith("SHA256SUMS.txt")){string hash=BadChecksum?new string('0',64):Convert.ToHexString(SHA256.HashData(Zip));content=new StringContent(hash+"  "+name+"\n");}
        else {if(CancelArchive)throw new OperationCanceledException("synthetic archive cancellation");content=new ByteArrayContent(Zip);}
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content,RequestMessage=request});
    }
}
