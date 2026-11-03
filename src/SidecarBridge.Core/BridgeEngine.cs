using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace SidecarBridge.Core;

public sealed class BridgeEngine
{
    private readonly Registry _registry;
    private readonly SemaphoreSlim _stateGate = new(1);
    private readonly SemaphoreSlim[] _scopeGates = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1)).ToArray();
    private readonly ConditionalWeakTable<object, ConcurrentDictionary<string, CachedPlan>> _scans = new();
    private readonly ConcurrentQueue<ReportEntry> _reports = new();
    private sealed record CachedPlan(ScopeSnapshot Snapshot, PlanResult Plan, string Rules);
    public BridgeEngine(string statePath) => _registry = new Registry(statePath);
    public IReadOnlyList<ReportEntry> RecentReports => _reports.ToArray();

    public async Task<ProcessResult> ProcessAsync(string root, string scope, string video, BridgeSettings settings, object scan, bool preview, CancellationToken ct)
    {
        PathSafety.Validate(root, video);
        if (!PathSafety.IsWithin(scope, video)) throw new IOException("Video is outside this search scope.");
        var gate = _scopeGates[(uint)PathSafety.Comparer.GetHashCode(Path.GetFullPath(scope)) % _scopeGates.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(settings)));
            var scopes = _scans.GetOrCreateValue(scan);
            var key = Path.GetFullPath(scope);
            if (preview || !scopes.TryGetValue(key, out var cached) || cached.Rules != rules || !cached.Snapshot.IsCurrent())
            {
                var snapshot = Discovery.Read(root, scope, settings, ct);
                cached = new(snapshot, Planner.Build(root, scope, snapshot, settings, ct), rules);
                if (!preview)
                {
                    if (scopes.Count >= 128 || scopes.Values.Sum(s => s.Snapshot.Videos.Length + s.Snapshot.Sidecars.Length + s.Snapshot.DirectoryStamps.Count) >= 100000) scopes.Clear();
                    scopes[key] = cached;
                }
            }
            var selected = cached.Plan.Links.Where(p => PathSafety.Comparer.Equals(p.Video, video)).ToArray();
            var report = new List<ReportEntry>(cached.Plan.Skipped);
            var changed = new List<string>();
            await _stateGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                foreach (var plan in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    try { Apply(root, plan, rules, settings, preview, report, changed); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
                    { report.Add(new(plan.Source, plan.Destination, "Skipped", ex.Message)); }
                }
                Cleanup(root, scope, video, settings, preview, report, changed, ct);
                var needsProbe = !preview && (_registry.State.PendingProbe.Contains(video) || changed.Count > 0);
                // Avoid another recursive walk after our own mutations. Each normal scan
                // has a new weakly held context; directory stamps catch other arrivals.
                if (!preview)
                    foreach (var directory in changed.Select(p => Path.GetDirectoryName(p)!).Distinct(PathSafety.Comparer))
                        if (cached.Snapshot.DirectoryStamps.ContainsKey(directory)) cached.Snapshot.DirectoryStamps[directory] = Directory.GetLastWriteTimeUtc(directory).Ticks;
                foreach (var item in report)
                {
                    _reports.Enqueue(item);
                    while (_reports.Count > 1000) _reports.TryDequeue(out _);
                }
                var expected = report.Where(e => e.Status is "Created" or "Existing" or "Replaced").Select(e => e.Destination).Distinct(PathSafety.Comparer).ToArray();
                return new(report, changed.Distinct(PathSafety.Comparer).ToArray(), needsProbe, expected);
            }
            finally { _stateGate.Release(); }
        }
        finally { gate.Release(); }
    }
    private void MarkPending(string video)
    {
        if (_registry.State.PendingProbe.Add(video)) _registry.Save();
    }
    public async Task AcknowledgeProbeAsync(string video, CancellationToken ct)
    {
        await _stateGate.WaitAsync(ct).ConfigureAwait(false);
        try { if (_registry.State.PendingProbe.Remove(video)) _registry.Save(); }
        finally { _stateGate.Release(); }
    }
    private void Apply(string root, LinkPlan plan, string rules, BridgeSettings settings, bool preview, List<ReportEntry> report, List<string> changed)
    {
        PathSafety.Validate(root, plan.Source); PathSafety.Validate(root, plan.Destination);
        if (_registry.State.Links.Any(l => PathSafety.Comparer.Equals(l.Destination, plan.Source)))
        { report.Add(new(plan.Source, plan.Destination, "Skipped", "Plugin-owned destinations cannot become new sources")); return; }
        // Holding this read handle denies concurrent writes/replacement on Windows.
        using var read = new FileStream(plan.Source, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sourceId = NativeFiles.GetIdentity(plan.Source);
        var sourceInfo = new FileInfo(plan.Source);
        var length = sourceInfo.Length;
        var modified = sourceInfo.LastWriteTimeUtc.Ticks;
        if (length == 0 || DateTime.UtcNow - sourceInfo.LastWriteTimeUtc < TimeSpan.FromSeconds(Math.Clamp(settings.MinimumAgeSeconds, 1, 86400)))
        { report.Add(new(plan.Source, plan.Destination, "Deferred", "Empty or recently written source; reconsider on the next normal scan")); return; }
        _ = read.ReadByte();
        if (read.Length != length || NativeFiles.GetIdentity(plan.Source) != sourceId || File.GetLastWriteTimeUtc(plan.Source).Ticks != modified)
        { report.Add(new(plan.Source, plan.Destination, "Deferred", "Source changed during inspection")); return; }
        var owned = _registry.State.Links.FirstOrDefault(l => PathSafety.Comparer.Equals(l.Destination, plan.Destination));
        if (File.Exists(plan.Destination))
        {
            var destinationId = NativeFiles.GetIdentity(plan.Destination);
            if (destinationId == sourceId)
            {
                report.Add(new(plan.Source, plan.Destination, "Existing", "Same filesystem file identity; existing unowned links are never adopted"));
                if (!preview && owned is not null && owned.Identity == destinationId && (owned.Length != length || owned.ModifiedTicks != modified))
                {
                    MarkPending(plan.Video); owned.Length = length; owned.ModifiedTicks = modified; _registry.Save(); changed.Add(plan.Destination);
                }
                return;
            }
            if (owned is null || owned.Identity != destinationId || !PathSafety.Comparer.Equals(owned.Source, plan.Source))
            { report.Add(new(plan.Source, plan.Destination, "Conflict", "Destination is not the recorded owned file; no overwrite")); return; }
            if (preview) { report.Add(new(plan.Source, plan.Destination, "PlannedReplacement", "Source identity changed; old destination still matches ownership registry")); return; }
            MarkPending(plan.Video);
            PathSafety.Validate(root, plan.Destination);
            if (NativeFiles.GetIdentity(plan.Destination) != owned.Identity) throw new IOException("Destination changed concurrently; replacement aborted.");
            File.Delete(plan.Destination);
            changed.Add(plan.Destination);
        }
        else if (preview) { report.Add(new(plan.Source, plan.Destination, "Planned", plan.Reason)); return; }
        MarkPending(plan.Video);
        PathSafety.Validate(root, plan.Source); PathSafety.Validate(root, plan.Destination);
        if (NativeFiles.GetIdentity(plan.Source) != sourceId) throw new IOException("Source replaced before linking; deferred.");
        try { NativeFiles.CreateLink(plan.Source, plan.Destination); }
        catch (IOException) when (File.Exists(plan.Destination))
        { report.Add(new(plan.Source, plan.Destination, "Conflict", "Destination was created concurrently; no ownership was acquired")); return; }
        if (NativeFiles.GetIdentity(plan.Destination) != sourceId) throw new IOException("Link identity verification failed; destination was not adopted.");
        if (owned is not null) _registry.State.Links.Remove(owned);
        _registry.State.Links.Add(new() { Source = plan.Source, Destination = plan.Destination, Video = plan.Video,
            Identity = sourceId, Length = length, ModifiedTicks = modified, RulesVersion = "1:" + rules });
        _registry.Save();
        changed.Add(plan.Destination);
        report.Add(new(plan.Source, plan.Destination, owned is null ? "Created" : "Replaced", plan.Reason));
    }
    private void Cleanup(string root, string scope, string video, BridgeSettings settings, bool preview, List<ReportEntry> report, List<string> changed, CancellationToken ct)
    {
        if (!settings.RemoveStaleOwnedLinks) return;
        foreach (var old in _registry.State.Links.Where(l => PathSafety.Comparer.Equals(l.Video, video) && PathSafety.IsWithin(scope, l.Source) && !File.Exists(l.Source)).ToArray())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PathSafety.Validate(root, old.Source); PathSafety.Validate(root, old.Destination);
                // File.Exists returns false on access failure too. Require a confirmed missing path.
                try { _ = File.GetAttributes(old.Source); continue; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (!File.Exists(old.Destination) || NativeFiles.GetIdentity(old.Destination) != old.Identity) continue;
                report.Add(new(old.Source, old.Destination, preview ? "PlannedRemoval" : "Removed", "Missing source; verified owned identity; cleanup explicitly enabled"));
                if (!preview)
                {
                    MarkPending(video);
                    PathSafety.Validate(root, old.Destination);
                    if (NativeFiles.GetIdentity(old.Destination) != old.Identity) continue;
                    File.Delete(old.Destination);
                    _registry.State.Links.Remove(old); _registry.Save(); changed.Add(old.Destination);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { report.Add(new(old.Source, old.Destination, "Skipped", ex.Message)); }
        }
    }
}
