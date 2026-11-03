using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SidecarBridge;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer)
    {
        Instance = this;
        StatePath = Path.Combine(paths.DataPath, "sidecar-bridge");
    }
    public string StatePath { get; }
    public override string Name => "Sidecar Bridge";
    public override Guid Id => new("89654c7e-bf44-4e9f-8adf-2219fe61fa36");
    public override string Description => "Connect nested external audio and subtitles with hardlinks during library scans.";
    public IEnumerable<PluginPageInfo> GetPages() => [new() { Name = "SidecarBridge", EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html" }];
}

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string[] EnabledLibraryIds { get; set; } = [];
    public global::SidecarBridge.Core.BridgeSettings Rules { get; set; } = new();
}
