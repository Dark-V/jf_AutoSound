namespace SidecarBridge.Core;

public sealed class BridgeSettings
{
    public bool EnableAudio { get; set; } = true;
    public bool EnableSubtitles { get; set; } = true;
    public int MaximumDepth { get; set; } = 4;
    public int MaximumEntries { get; set; } = 20000;
    public int MinimumAgeSeconds { get; set; } = 30;
    public int ScopeParentLevels { get; set; }
    public bool RemoveStaleOwnedLinks { get; set; }
    public DirectoryRule[] DirectoryRules { get; set; } = [];
    public ExplicitMapping[] ExplicitMappings { get; set; } = [];
}

public sealed class DirectoryRule
{
    public string Pattern { get; set; } = "";
    public string Language { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Default { get; set; }
    public bool Forced { get; set; }
    public bool HearingImpaired { get; set; }
    public string MatchPattern { get; set; } = "";
    public string MatchReplacement { get; set; } = "";
}
public sealed class ExplicitMapping
{
    public string Source { get; set; } = "";
    public string Video { get; set; } = "";
}
public sealed record LinkPlan(string Source, string Destination, string Reason, string Video = "");
public sealed record ReportEntry(string Source, string Destination, string Status, string Reason);
public sealed record ProcessResult(IReadOnlyList<ReportEntry> Entries, IReadOnlyList<string> ChangedPaths, bool RequiresProbe, IReadOnlyList<string>? ExpectedPaths = null);
