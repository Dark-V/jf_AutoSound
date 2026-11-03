# Integration and architecture

## Exact target and evidence

Jellyfin tag **v12.4**, commit **83d0d0e7f76e23e2e085c8ff67d28b772d35967d**; server `12.4.0.0`, ABI `12.4.0.0`; `net10.0`. The latest stable release was selected after the installed-server endpoint could not be inspected in the initial sandbox. No production server was modified or used for the test.

These are the owning source files reviewed at that tag:

| Source | What matters |
| --- | --- |
| [`IPreRefreshProvider`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/IPreRefreshProvider.cs) | Marker for custom providers that run before regular metadata work |
| [`ICustomMetadataProvider`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/ICustomMetadataProvider.cs), [`IHasItemChangeMonitor`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/IHasItemChangeMonitor.cs), [`IHasOrder`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/IHasOrder.cs) | Typed current-refresh work, change selection and ordering |
| [`MetadataService.GetProviders`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/Manager/MetadataService.cs#L647) | Provider filtering occurs before `FetchAsync`; changed pre-refresh providers do not automatically select every custom provider |
| [`RefreshWithProviders`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/Manager/MetadataService.cs#L780) | Selected pre-refresh providers run first; ordinary custom providers run later |
| [`ProbeProvider.HasChanged`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/MediaInfo/ProbeProvider.cs#L127) | Compares current video metadata and existing external-file lists; before links exist it may report unchanged |
| [`DirectoryService`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/DirectoryService.cs) / [`IDirectoryService`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Controller/Providers/IDirectoryService.cs) | Separate directory-entry, single-entry and file-path caches; public `Invalidate` removes a path and its parent |
| [`AudioResolver`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/MediaInfo/AudioResolver.cs), [`SubtitleResolver`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/MediaInfo/SubtitleResolver.cs), [`MediaInfoResolver`](https://github.com/jellyfin/jellyfin/blob/v12.4/MediaBrowser.Providers/MediaInfo/MediaInfoResolver.cs) | Consume same-folder external paths through the directory service and use the native media encoder |
| [`ExternalPathParser`](https://github.com/jellyfin/jellyfin/blob/v12.4/Emby.Naming/ExternalFiles/ExternalPathParser.cs) | Language/title/flag semantics, including substring flag matching |

## Why an early callback alone fails

On an old unchanged video, the new source is still nested in a subfolder. The normal probe change monitor sees no new same-folder external files and can be excluded. Returning `true` from our monitor ensures **our** provider runs, but does not ensure the native custom `ProbeProvider` runs. Creating links in `FetchAsync` therefore cannot by itself prove registration during that pass. Clearing only file-path cache is also insufficient for other cached directory observations.

## The implemented public-API route

1. `BridgeProvider` implements typed custom providers for `Video`, `Movie`, `Episode` and `MusicVideo`, plus `IPreRefreshProvider`, `IHasItemChangeMonitor` and order `-1000`.
2. Its change monitor is read-only and returns true for enabled local video items. Processing rechecks current filesystem state on each suitable pass.
3. The pre-refresh callback runs discovery → matching → plan → hardlink application.
4. Each changed path invalidates the supplied **current refresh's** `IDirectoryService`, including its parent directory.
5. If links/identities changed or a previous probe was interrupted, the plugin finds Jellyfin's native typed `ProbeProvider` through public `IProviderManager.GetMetadataProviders<T>` and invokes its public `FetchAsync` with the **same item, options and cancellation token**.
6. Native probing writes `MediaStreams` through Jellyfin. The plugin reads the public media-stream repository and acknowledges pending work only after all expected paths are registered. A failed/partial native probe remains pending until a future ordinary scan.

There is no `RefreshMetadata`, refresh queue call, `ItemAdded` handler, script, reflection patch or second scan. The plugin does not invoke an external process or manufacture `MediaStream` records. Looking up the native implementation by its exact full type name is deliberately version-specific and fails with a diagnostic if that provider is absent.

On an initial/full refresh Jellyfin may also have selected its ordinary probe. Native probing can therefore run twice within that **one** refresh. This trades a bounded amount of native work after actual link changes for correct provider-selection behavior; unchanged successful passes do not request the extra native probe. This is a documented cost, not a claim of exactly one ffprobe process.

## Test sequence

The real-server test starts a disposable server built from the pinned tag. It disables library realtime monitoring and automatic metadata interval refresh, generates a video/audio/subtitle fixture, enables the plugin, issues **one ordinary scheduled library scan**, waits for that task to finish, and reads the item API with `MediaStreams`.

It then adds a new audio file without changing the video, issues **one** more ordinary scan, and verifies both the new stream record and file identity. Additional cases cover source replacement, an unchanged follow-up scan, preview and access control. The first A/B prototype passed before the matcher/settings expansion; the full implementation is tested separately. See [verification](verification.md).

## Core boundaries and resource limits

`SidecarBridge.Core` has no Jellyfin dependency: `Discovery`, `Matching`, `Planner`, `TrackNaming`, `NativeFiles`, `Registry`, `BridgeEngine`. The plugin assembly owns library selection, configuration, admin API and native-provider coordination.

Scope requests coalesce under 256 bounded striped semaphores. A scan context is weakly held. It caches at most 128 scopes/about 100000 records before eviction; directory timestamps trigger invalidation, and every new scan gets its own context. Scope snapshots index video names/episode keys once rather than recursively scanning for each episode. Applying a link independently rechecks source identity/readiness. Directory/path limits and cancellation prevent unbounded walks.

Registry writes are serialized and replaced atomically from flushed temporary files. Pending-probe state is persisted before link mutation. If a crash occurs between link creation and ownership persistence, the link is left **unowned**, never silently adopted. Automatic missing-source deletion starts disabled. The registry records source, destination, video, filesystem identity, length, mtime and rules fingerprint; filesystem checks remain authoritative.
