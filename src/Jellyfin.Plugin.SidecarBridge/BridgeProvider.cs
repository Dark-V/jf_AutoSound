using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SidecarBridge.Core;

namespace Jellyfin.Plugin.SidecarBridge;

// HasChanged is deliberately read-only. FetchAsync runs the native probe in THIS refresh:
// MetadataService has already filtered the usual ProbeProvider on unchanged videos.
public sealed class BridgeProvider(ILibraryManager library, IProviderManager providers, IMediaStreamRepository streams, ILogger<BridgeProvider> logger)
    : IPreRefreshProvider, IHasItemChangeMonitor, IHasOrder,
      ICustomMetadataProvider<Video>, ICustomMetadataProvider<Movie>,
      ICustomMetadataProvider<Episode>, ICustomMetadataProvider<MusicVideo>
{
    private static readonly object EngineLock = new();
    private static BridgeEngine? _engine;
    public static BridgeEngine Engine
    {
        get { lock (EngineLock) return _engine ??= new BridgeEngine(Plugin.Instance.StatePath); }
    }
    public string Name => "Sidecar Bridge";
    public int Order => -1000;

    private (string Root, string Scope)? Context(BaseItem item)
    {
        if (item is not Video || !item.IsFileProtocol || string.IsNullOrEmpty(item.Path)) return null;
        var enabled = Plugin.Instance.Configuration.EnabledLibraryIds;
        var folder = library.GetVirtualFolders().FirstOrDefault(f =>
            enabled.Any(id => string.Equals(id.Replace("-", ""), f.ItemId.Replace("-", ""), StringComparison.OrdinalIgnoreCase)) &&
            f.Locations.Any(root => PathSafety.IsWithin(root, item.Path)));
        var root = folder?.Locations.Where(r => PathSafety.IsWithin(r, item.Path)).OrderByDescending(r => r.Length).FirstOrDefault();
        if (root is null) return null;
        var parent = Path.GetDirectoryName(item.Path)!;
        var scope = Path.GetFileName(parent).Equals("Video", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(parent)! : parent;
        for (int i = 0; i < Math.Clamp(Plugin.Instance.Configuration.Rules.ScopeParentLevels, 0, 3); i++)
        {
            var ancestor = Path.GetDirectoryName(scope);
            if (ancestor is null || !PathSafety.IsWithin(root, ancestor)) break;
            scope = ancestor;
        }
        return PathSafety.IsWithin(root, scope) ? (root, scope) : null;
    }

    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        var context = Context(item);
        if (context is null) return false;
        // Always recheck enabled items. Scope discovery is shared
        // by this scan's directory service; no permanent done marker or TTL hides arrivals.
        return true;
    }

    private async Task<ItemUpdateType> Fetch<T>(T item, MetadataRefreshOptions options, CancellationToken ct) where T : Video
    {
        var context = Context(item);
        if (context is null) return ItemUpdateType.None;
        try
        {
            var result = await Engine.ProcessAsync(context.Value.Root, context.Value.Scope, item.Path,
                Plugin.Instance.Configuration.Rules, options.DirectoryService, false, ct).ConfigureAwait(false);
            foreach (var path in result.ChangedPaths) options.DirectoryService.Invalidate(path);
            if (!result.RequiresProbe) return ItemUpdateType.None;
            var probe = providers.GetMetadataProviders<T>(item, library.GetLibraryOptions(item))
                .OfType<ICustomMetadataProvider<T>>()
                .FirstOrDefault(p => p.GetType().FullName == "MediaBrowser.Providers.MediaInfo.ProbeProvider");
            if (probe is null) throw new InvalidOperationException("Jellyfin 12.4 native ProbeProvider is unavailable; links exist but stream registration is unconfirmed.");
            // No RefreshMetadata, queue, second scan, reflection, or external process.
            var update = await probe.FetchAsync(item, options, ct).ConfigureAwait(false);
            var registered = streams.GetMediaStreams(new MediaStreamQuery { ItemId = item.Id })
                .Where(s => s.IsExternal).Select(s => s.Path).ToHashSet(PathSafety.Comparer);
            if ((result.ExpectedPaths ?? []).All(registered.Contains))
                await Engine.AcknowledgeProbeAsync(item.Path, ct).ConfigureAwait(false);
            else logger.LogWarning("Sidecar Bridge: native probe did not register every expected stream for {Item}; will retry on the next normal scan", SafeLog(item.Path));
            return update;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning("Sidecar Bridge skipped item {Item}: {Reason}", SafeLog(item.Path), SafeLog(ex.Message));
            return ItemUpdateType.None;
        }
    }
    private static string SafeLog(string text) => string.Concat(text.Select(c => char.IsControl(c) ? ' ' : c));
    public Task<ItemUpdateType> FetchAsync(Video item, MetadataRefreshOptions options, CancellationToken ct) => Fetch(item, options, ct);
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken ct) => Fetch(item, options, ct);
    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken ct) => Fetch(item, options, ct);
    public Task<ItemUpdateType> FetchAsync(MusicVideo item, MetadataRefreshOptions options, CancellationToken ct) => Fetch(item, options, ct);
}
