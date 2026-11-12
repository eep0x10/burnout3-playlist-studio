using System.Reflection;
using System.Text.Json;
using Burnout3.PlaylistStudio.App;
using Burnout3.PlaylistStudio.Core;

internal static class UpdateCoordinatorChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        var skipped = new Backend();
        await using (var coordinator = Make(skipped, false, false, _ => true, (_, _) => throw new Exception("Unexpected handoff")))
        {
            await coordinator.CheckAsync(); check(skipped.Checks == 0, "disabled automatic check does not access backend");
        }
        var manual = new Backend(); int handoffs = 0;
        await using (var coordinator = Make(manual, false, false, _ => true, (_, _) => handoffs++))
        {
            await coordinator.CheckAsync(true);
            check(manual.IgnoreCooldown && handoffs == 1, "explicit manual install works with automatic updates off");
            check(manual.Restart && manual.Arguments.SequenceEqual(new[] { "--data", "C:/Demo/data", "--config", "C:/Demo/settings.toml", "--minimized" }), "validated restart arguments forwarded to staging");
            coordinator.TryApply(); check(handoffs == 1, "handoff occurs only once");
        }
        var delayed = new Backend(); bool safe = false; handoffs = 0;
        await using (var coordinator = Make(delayed, true, false, _ => safe, (_, _) => handoffs++))
        {
            await coordinator.CheckAsync(); check(coordinator.HasPendingUpdate && handoffs == 0, "unsafe application remains pending");
            safe = true; check(coordinator.TryApply() && handoffs == 1, "idle retry applies already staged update");
        }
        var companion = new Backend(); handoffs = 0; bool closingHandoff = false;
        await using (var coordinator = Make(companion, true, true, _ => true, (_, closing) => { handoffs++; closingHandoff = closing; }))
        {
            await coordinator.CheckAsync(); check(handoffs == 0 && !companion.Restart, "companion never hands off while open and stages without restart");
            await coordinator.StopAsync(); check(coordinator.TryApply(true) && handoffs == 1 && closingHandoff, "companion applies on safe close without restarting");
        }
        var converted = new Backend(); handoffs = 0;
        await using (var coordinator = Make(converted, true, false, _ => false, (_, _) => handoffs++))
        {
            await coordinator.CheckAsync(); coordinator.EnterCompanionSession(); await coordinator.CheckAsync();
            check(coordinator.CompanionSession && !converted.Restart && handoffs == 0, "launcher IPC replaces independent stage with companion stage");
        }
        var reserved = new Backend(); handoffs = 0;
        await using (var coordinator = Make(reserved, true, true, _ => false, (_, _) => handoffs++))
        {
            await coordinator.CheckAsync();
            check(coordinator.HasPendingUpdate && handoffs == 0, "companion can stage while launch reservation prevents application");
            await coordinator.StopAsync();
            check(!coordinator.TryApply(true) && handoffs == 0, "unsafe companion close keeps verified update unapplied");
        }
        var interruptedCompanion = new Backend { HoldStage = true }; handoffs = 0;
        await using (var coordinator = Make(interruptedCompanion, true, true, _ => true, (_, _) => handoffs++))
        {
            Task job = coordinator.CheckAsync(); await interruptedCompanion.StageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); coordinator.Cancel(); await job;
            check(!coordinator.HasPendingUpdate && !coordinator.TryApply(true) && handoffs == 0, "cancelled companion stage cannot apply on close");
        }
        var failed = new Backend { FailStage = true }; handoffs = 0;
        await using (var coordinator = Make(failed, true, false, _ => true, (_, _) => handoffs++))
        {
            await coordinator.CheckAsync(); check(handoffs == 0 && !coordinator.HasPendingUpdate && !coordinator.IsWorking, "failed download never closes manager");
        }
        var cancelled = new Backend { HoldStage = true }; handoffs = 0;
        await using (var coordinator = Make(cancelled, true, false, _ => true, (_, _) => handoffs++))
        {
            Task job = coordinator.CheckAsync(); await cancelled.StageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); coordinator.SetAutomatic(false); await job;
            check(handoffs == 0 && !coordinator.HasPendingUpdate && !coordinator.IsWorking, "disabling automatic update cancels its download");
        }
        var shutdown = new Backend { HoldStage = true }; handoffs = 0;
        await using (var coordinator = Make(shutdown, true, false, _ => true, (_, _) => handoffs++))
        {
            _ = coordinator.CheckAsync(); await shutdown.StageEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)); await coordinator.StopAsync();
            check(handoffs == 0 && !coordinator.IsWorking, "shutdown waits for cancellation without handoff");
        }
        var offline = new Backend { FailCheck = true };
        await using (var coordinator = Make(offline, true, false, _ => true, (_, _) => throw new Exception("Unexpected handoff")))
        {
            await coordinator.CheckAsync(); check(!coordinator.IsWorking && coordinator.Status.Contains("indisponível"), "offline check becomes nonblocking status");
        }
        var spawnFailure = new Backend();
        await using (var coordinator = Make(spawnFailure, true, false, _ => true, (_, _) => throw new IOException("Fixture spawn denied")))
        {
            await coordinator.CheckAsync(); check(!coordinator.HasPendingUpdate && coordinator.Status.Contains("iniciar"), "failed helper startup preserves manager and avoids retry loop");
        }
        var oldSettings = typeof(StudioForm).GetNestedType("UiSettings", BindingFlags.NonPublic)!;
        object preferences = JsonSerializer.Deserialize("{\"TakedawnExecutable\":\"C:/Demo/Takedawn.exe\",\"SongNoticeEnabled\":false}", oldSettings)!;
        check((bool)oldSettings.GetProperty("AutomaticUpdates")!.GetValue(preferences)!, "legacy preferences default automatic updates on");
        string serialized = JsonSerializer.Serialize(preferences, oldSettings);
        check(serialized.Contains("C:/Demo/Takedawn.exe") && serialized.Contains("\"SongNoticeEnabled\":false"), "new preference preserves executable and notice setting");
    }
    private static UpdateCoordinator Make(Backend backend, bool automatic, bool companion, Func<bool, bool> safe, Action<StagedUpdate, bool> handoff)
        => new(new Version(1, 3, 0), automatic, companion, ["--data", "C:/Demo/data", "--config", "C:/Demo/settings.toml", "--minimized"], safe, handoff, backend);
    private sealed class Backend : IPortableUpdateBackend
    {
        internal int Checks;
        internal bool IgnoreCooldown, Restart, FailStage, FailCheck, HoldStage;
        internal IReadOnlyList<string> Arguments = [];
        internal TaskCompletionSource StageEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly UpdateRelease release = new(new Version(1, 4, 0), "v1.4.0", "fixture.zip", "https://example.invalid/fixture", "https://example.invalid/checksums", "https://example.invalid/release");
        public Task<UpdateCheckResult> CheckAsync(Version current, CancellationToken cancellationToken, bool ignoreCooldown)
        {
            Checks++; IgnoreCooldown = ignoreCooldown;
            if (FailCheck) throw new IOException("Fixture offline");
            return Task.FromResult(new UpdateCheckResult(current, release.Version, true, false, "Fixture release", release));
        }
        public async Task<StagedUpdate> StageAsync(UpdateRelease selected, int parentId, DateTime parentStartUtc, IProgress<UpdateProgress> progress, CancellationToken cancellationToken, IReadOnlyList<string> restartArguments, bool restartAfterApply)
        {
            Arguments = restartArguments.ToArray(); Restart = restartAfterApply; StageEntered.TrySetResult();
            if (HoldStage) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailStage) throw new IOException("Fixture incomplete download");
            return new("C:/Fixture/update-plan.json", "C:/Fixture/worker.exe", selected.Version, "C:/Fixture/program");
        }
    }
}
