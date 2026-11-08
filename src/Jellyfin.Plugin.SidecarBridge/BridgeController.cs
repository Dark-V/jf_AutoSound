using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SidecarBridge.Core;

namespace Jellyfin.Plugin.SidecarBridge;

[ApiController]
[Route("SidecarBridge")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class BridgeController(ILibraryManager library) : ControllerBase
{
    [HttpGet("Libraries")]
    public object Libraries() => library.GetVirtualFolders().Select(f => new { Id = f.ItemId, f.Name, f.Locations });

    [HttpGet("Report")]
    public object Report() => BridgeProvider.Engine.RecentReports;

    public sealed class PreviewRequest
    {
        public string LibraryId { get; set; } = "";
        public string VideoPath { get; set; } = "";
        public string? OnlyFolder { get; set; }
        public BridgeSettings? Rules { get; set; }
    }
    [HttpPost("Preview")]
    public async Task<ActionResult<ProcessResult>> Preview([FromBody] PreviewRequest request, CancellationToken ct)
    {
        var folder = library.GetVirtualFolders().FirstOrDefault(f => string.Equals(f.ItemId.Replace("-", ""), request.LibraryId.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
        var root = folder?.Locations.Where(r => PathSafety.IsWithin(r, request.VideoPath)).OrderByDescending(r => r.Length).FirstOrDefault();
        if (root is null || !System.IO.File.Exists(request.VideoPath) || Path.GetExtension(request.VideoPath).ToLowerInvariant() is not (".mkv" or ".mp4"))
            return BadRequest("Choose an existing MKV/MP4 inside the selected Jellyfin library.");
        root = BridgeProvider.RestrictRoot(root, request.OnlyFolder);
        if (root is null || !PathSafety.IsWithin(root, request.VideoPath))
            return BadRequest("Choose a video inside the folder limit and the selected library.");
        try
        {
            var rules = request.Rules ?? Plugin.Instance.Configuration.Rules;
            var scope = BridgeProvider.SearchScope(root, request.VideoPath, rules.ScopeParentLevels);
            return Ok(await BridgeProvider.Engine.ProcessAsync(root, scope, request.VideoPath, rules, new object(), true, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return BadRequest(ex.Message); }
    }
}
