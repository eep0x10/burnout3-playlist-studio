using Burnout3.PlaylistStudio.App;
using Burnout3.PlaylistStudio.Core;

// Only injectable fakes: no windows, game process, filesystem or network operations.
int passed = 0;
void Check(bool value, string name)
{
    if (!value) throw new Exception(name);
    passed++; Console.WriteLine("PASS " + name);
}
async Task Reject(Fake service, string name)
{
    bool rejected = false;
    try { await PrelaunchSync.PrepareAsync(service, null, "demo.exe"); }
    catch (InvalidOperationException) { rejected = true; }
    Check(rejected, name);
}

var ready = new Fake();
var result = await PrelaunchSync.PrepareAsync(ready, "demo.toml", "demo.exe");
Check(result.AppliedToGame, "success applied");
Check(ready.Calls == 1, "single sync");
Check(ready.Config == "demo.toml" && ready.Game == "demo.exe", "paths forwarded");
var running = new Fake { AlreadyRunning = true };
await Reject(running, "running guard");
Check(running.Calls == 0 && running.ActiveReads == 0, "running guard before library access");
var empty = new Fake { NoActive = true };
await Reject(empty, "no active rejected");
Check(empty.Calls == 0, "no active no sync");
await Reject(new Fake { Result = new(0, 0, true, 0, 1, false, false, []) }, "failed rejected");
await Reject(new Fake { Result = new(0, 0, true, 1, 0, false, true, []) }, "pending rejected");
await Reject(new Fake { Result = new(0, 0, true, 1, 0, false, false, []) }, "not applied rejected");
await Reject(new Fake { StartsDuring = true }, "game starts during rejected");
bool sourceError = false;
try { await PrelaunchSync.PrepareAsync(new Fake { Throw = true }, null, "demo.exe"); }
catch (IOException) { sourceError = true; }
Check(sourceError, "service error propagated");
var partial = await PrelaunchSync.PrepareAsync(new Fake { Result = new(1, 0, false, 1, 0, true, false, []) }, null, "demo.exe");
Check(!partial.Complete, "partial exposed for explicit UI decision");
using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
var cancelled = new Fake(); bool cancelledResult = false;
try { await PrelaunchSync.PrepareAsync(cancelled, null, "demo.exe", cancellationToken: cancellation.Token); }
catch (OperationCanceledException) { cancelledResult = true; }
Check(cancelledResult && cancelled.Calls == 0, "cancel before side effects");
using var during = new CancellationTokenSource(); bool interrupted = false;
try { await PrelaunchSync.PrepareAsync(new Fake { During = during.Cancel }, null, "demo.exe", cancellationToken: during.Token); }
catch (OperationCanceledException) { interrupted = true; }
Check(interrupted, "cancel during sync cannot permit launch");
Console.WriteLine($"{passed} prelaunch checks passed.");

sealed class Fake : IPrelaunchSyncService
{
    public bool AlreadyRunning, NoActive, StartsDuring, Throw;
    public int Calls, ActiveReads, Checks;
    public string? Config, Game;
    public Action? During;
    public SyncResult Result = new(0, 0, true, 1, 0, true, false, []);
    public bool IsGameRunning() => AlreadyRunning || (StartsDuring && ++Checks > 1);
    public Guid? ActivePlaylistId { get { ActiveReads++; return NoActive ? null : Guid.Parse("11111111-1111-1111-1111-111111111111"); } }
    public Task<SyncResult> SyncAsync(Guid id, string? config, string game, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        Calls++; Config = config; Game = game;
        if (Throw) throw new IOException("Fixture service locked");
        During?.Invoke(); return Task.FromResult(Result);
    }
}
