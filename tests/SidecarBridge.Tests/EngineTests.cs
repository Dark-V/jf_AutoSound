using System.Text.Json;
using SidecarBridge.Core;
using Xunit;

namespace SidecarBridge.Tests;

public sealed class EngineTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sidecar-tests-" + Guid.NewGuid().ToString("N"));
    private string State => Path.Combine(_root, "state");
    private string Scope => Path.Combine(_root, "media");
    private string Video => Path.Combine(Scope, "Video", "Film.mkv");
    private string Source => Path.Combine(Scope, "Rus Sound", "NDA Studio", "Film.mka");
    public EngineTests() { Write(Video); Write(Source); }
    private static void Write(string path, string text = "test-media-content")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
    }
    private Task<ProcessResult> Run(BridgeEngine engine, BridgeSettings? settings = null, bool preview = false, object? scan = null, CancellationToken ct = default)
        => engine.ProcessAsync(_root, Scope, Video, settings ?? new(), scan ?? new object(), preview, ct);
    [Fact]
    public async Task IdempotentIdentityAndOriginalBytes()
    {
        var original = File.ReadAllBytes(Source); var engine = new BridgeEngine(State);
        var first = await Run(engine); var link = Assert.Single(first.ChangedPaths);
        Assert.True(NativeFiles.Same(Source, link));
        await engine.AcknowledgeProbeAsync(Video, default);
        var state = File.ReadAllBytes(Path.Combine(State, "links.json"));
        var again = await Run(engine);
        Assert.Empty(again.ChangedPaths); Assert.False(again.RequiresProbe);
        Assert.Equal(state, File.ReadAllBytes(Path.Combine(State, "links.json")));
        Assert.Equal(original, File.ReadAllBytes(Source)); Assert.Equal("test-media-content", File.ReadAllText(Video));
    }
    [Fact]
    public async Task AddedLaterFoundEvenWithSameScanContext()
    {
        var engine = new BridgeEngine(State); var scan = new object(); await Run(engine, scan: scan);
        Write(Path.Combine(Scope, "English Audio", "Another Studio", "Film.mka"));
        var result = await Run(engine, scan: scan);
        Assert.Single(result.Entries, e => e.Status == "Created");
        Assert.Contains(result.Entries, e => e.Destination.EndsWith("Another Studio.eng.mka", StringComparison.Ordinal));
    }
    [Fact]
    public async Task ReplacedSourceUpdatesOnlyOwnedDestinationAfterRestart()
    {
        var first = await Run(new BridgeEngine(State)); var link = Assert.Single(first.ChangedPaths);
        var oldId = NativeFiles.GetIdentity(Source);
        File.Move(Source, Source + ".old"); Write(Source, "new-data");
        Assert.NotEqual(oldId, NativeFiles.GetIdentity(Source));
        var result = await Run(new BridgeEngine(State));
        Assert.Contains(result.Entries, e => e.Status == "Replaced");
        Assert.True(NativeFiles.Same(Source, link)); Assert.Equal("new-data", File.ReadAllText(link));
        Assert.Equal("test-media-content", File.ReadAllText(Source + ".old"));
    }
    [Fact]
    public async Task UserReplacementIsNeverOverwritten()
    {
        var engine = new BridgeEngine(State); var first = await Run(engine); var link = Assert.Single(first.ChangedPaths);
        File.Delete(link); Write(link, "user-file");
        var result = await Run(engine); Assert.Contains(result.Entries, e => e.Status == "Conflict");
        Assert.Equal("user-file", File.ReadAllText(link));
    }
    [Fact]
    public async Task ExistingForeignHardlinkIsNotAdopted()
    {
        var link = Path.Combine(Path.GetDirectoryName(Video)!, "Film.NDA Studio.rus.mka"); NativeFiles.CreateLink(Source, link);
        var engine = new BridgeEngine(State); var result = await Run(engine);
        Assert.Contains(result.Entries, e => e.Status == "Existing");
        File.Delete(Source);
        await Run(engine, new() { RemoveStaleOwnedLinks = true }); Assert.True(File.Exists(link));
    }
    [Fact]
    public async Task CollisionNeverChoosesAStudioArbitrarily()
    {
        Write(Path.Combine(Scope, "Audio", "NDA Studio", "Film.rus.mka"));
        var result = await Run(new BridgeEngine(State)); Assert.Empty(result.ChangedPaths);
        Assert.Equal(2, result.Entries.Count(e => e.Status == "Conflict"));
    }
    [Fact]
    public async Task PreviewDoesNotCreateRegistryOrLinks()
    {
        var result = await Run(new BridgeEngine(State), preview: true);
        Assert.Contains(result.Entries, e => e.Status == "Planned");
        Assert.False(Directory.Exists(State)); Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Video)!, "*.mka"));
    }
    [Fact]
    public async Task ParallelRequestsCreateOneLinkAndOneRegistryEntry()
    {
        var engine = new BridgeEngine(State);
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Run(engine)));
        Assert.Equal(1, results.Sum(r => r.Entries.Count(e => e.Status == "Created")));
        var registry = JsonSerializer.Deserialize<RegistryState>(File.ReadAllText(Path.Combine(State, "links.json")))!;
        Assert.Single(registry.Links);
    }
    [Fact]
    public async Task CancellationDoesNotPoisonSharedScope()
    {
        var engine = new BridgeEngine(State); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(engine, ct: cancel.Token));
        Assert.Single((await Run(engine)).ChangedPaths);
    }
    [Fact]
    public async Task CleanupIsOptInAndIdentityChecked()
    {
        var engine = new BridgeEngine(State); var link = Assert.Single((await Run(engine)).ChangedPaths);
        File.Delete(Source); await Run(engine); Assert.True(File.Exists(link));
        await Run(engine, new() { RemoveStaleOwnedLinks = true }); Assert.False(File.Exists(link));
        Assert.True(File.Exists(Video));
    }
    [Fact]
    public async Task InterruptedProbeRetriesAfterEngineRestart()
    {
        await Run(new BridgeEngine(State));
        var restarted = new BridgeEngine(State); var result = await Run(restarted);
        Assert.Empty(result.ChangedPaths); Assert.True(result.RequiresProbe);
        await restarted.AcknowledgeProbeAsync(Video, default);
        Assert.False((await Run(restarted)).RequiresProbe);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EmptyAndRecentFilesAreDeferred(bool empty)
    {
        File.WriteAllText(Source, empty ? "" : "still-copying");
        var result = await Run(new BridgeEngine(State)); Assert.Empty(result.ChangedPaths);
        Assert.Contains(result.Entries, e => e.Status == "Deferred");
    }
    [Fact]
    public async Task TooLargeScopeFailsWithoutPartialWrites()
    {
        await Assert.ThrowsAsync<IOException>(() => Run(new BridgeEngine(State), new() { MaximumEntries = 1 }));
        Assert.False(Directory.Exists(State));
    }
    [Fact]
    public async Task ExplicitMappingsAndLanguageRules()
    {
        File.Move(Source, Source + ".part");
        var source = Path.Combine(Scope, "Custom", "other-name.mka"); Write(source);
        var settings = new BridgeSettings { DirectoryRules = [new() { Pattern = "^Custom$", Title = "Studio", Language = "de" }],
            ExplicitMappings = [new() { Source = "Custom/other-name.mka", Video = "Video/Film.mkv" }] };
        var result = await Run(new BridgeEngine(State), settings);
        Assert.EndsWith("Film.Studio.deu.mka", Assert.Single(result.ChangedPaths));
    }
    [Fact]
    public async Task EscapingExplicitMappingIsRejected()
    {
        var settings = new BridgeSettings { ExplicitMappings = [new() { Source = "Rus Sound/NDA Studio/Film.mka", Video = "../../escape.mkv" }] };
        var result = await Run(new BridgeEngine(State), settings); Assert.Empty(result.ChangedPaths);
        Assert.Contains(result.Entries, e => e.Status == "Skipped" && e.Reason.Contains("escape", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void SymlinkOrJunctionIsRejected()
    {
        var alias = Path.Combine(_root, "alias");
        if (OperatingSystem.IsWindows())
        {
            // Junction creation does not require Windows symlink developer mode.
            var start = new System.Diagnostics.ProcessStartInfo("cmd", $"/c mklink /J \"{alias}\" \"{Scope}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
            using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(alias, Scope);
        Assert.Throws<IOException>(() => PathSafety.Validate(_root, Path.Combine(alias, "Video", "Film.mkv")));
        Directory.Delete(alias);
    }
    [Fact]
    public async Task MissingSourceDoesNotBlockAnotherTrack()
    {
        File.Delete(Source); Write(Path.Combine(Scope, "Audio", "Other", "Film.mka"));
        var result = await Run(new BridgeEngine(State)); Assert.Single(result.ChangedPaths);
    }
    public void Dispose()
    {
        // Test-owned unique temporary directory only.
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
