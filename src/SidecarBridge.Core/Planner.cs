using System.Text.RegularExpressions;

namespace SidecarBridge.Core;

public sealed record PlanResult(LinkPlan[] Links, ReportEntry[] Skipped);
public static class Planner
{
    public static PlanResult Build(string root, string scope, ScopeSnapshot snapshot, BridgeSettings settings, CancellationToken ct)
    {
        var plans = new List<LinkPlan>();
        var skipped = new List<ReportEntry>();
        var index = new Matching.VideoIndex(snapshot.Videos);
        foreach (var warning in snapshot.Warnings) skipped.Add(new("", "", "Skipped", warning));
        foreach (var source in snapshot.Sidecars)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var relative = Path.GetRelativePath(scope, source).Replace('\\', '/');
                var directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
                var rule = settings.DirectoryRules.FirstOrDefault(r => Regex.IsMatch(directory, r.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Matching.RegexTimeout));
                var mappings = settings.ExplicitMappings.Where(m => PathSafety.Comparer.Equals(m.Source.Replace('\\', '/'), relative)).ToArray();
                // Only nested, recognized track directories or explicit rules are sources.
                if (directory.Length == 0 || (!TrackNaming.IsTrackDirectory(directory) && rule is null && mappings.Length == 0)) continue;
                var parts = TrackNaming.Parse(source);
                var directoryInfo = TrackNaming.FromDirectory(directory);
                var stem = parts.Stem;
                if (rule is not null && !string.IsNullOrEmpty(rule.MatchPattern))
                    stem = Regex.Replace(stem, rule.MatchPattern, rule.MatchReplacement, RegexOptions.CultureInvariant, Matching.RegexTimeout);
                string? video;
                string reason;
                if (mappings.Length > 0)
                {
                    if (mappings.Length != 1) { skipped.Add(new(source, "", "Skipped", "Several explicit mappings for this source")); continue; }
                    video = Path.GetFullPath(Path.Combine(scope, mappings[0].Video));
                    PathSafety.Validate(root, video);
                    if (!PathSafety.IsWithin(scope, video) || !snapshot.Videos.Contains(video, PathSafety.Comparer)) throw new IOException("Explicit video mapping escapes the scope or does not name a discovered video.");
                    reason = "Explicit mapping";
                }
                else
                {
                    // A literal basename match takes priority even if a title happens to
                    // end in a language-looking word such as "It".
                    (video, reason) = index.Match(Path.GetFileNameWithoutExtension(source));
                    if (video is null && !reason.StartsWith("Ambiguous", StringComparison.Ordinal))
                        (video, reason) = index.Match(stem);
                }
                if (video is null) { skipped.Add(new(source, "", "Skipped", reason)); continue; }
                var language = parts.Language ?? (rule is not null && !string.IsNullOrEmpty(rule.Language) ? TrackNaming.Language(rule.Language) : directoryInfo.Language);
                if (rule is not null && !string.IsNullOrEmpty(rule.Language) && rule.Language != "und" && TrackNaming.Language(rule.Language) is null) throw new IOException("Unrecognized configured language; use an ISO language code or leave it empty.");
                var title = !string.IsNullOrWhiteSpace(rule?.Title) ? rule.Title : directoryInfo.Title;
                var destination = TrackNaming.Destination(video, source, title, language,
                    parts.Default || (rule?.Default ?? false), parts.Forced || (rule?.Forced ?? false), parts.HearingImpaired || (rule?.HearingImpaired ?? false));
                if (PathSafety.Comparer.Equals(source, destination)) continue;
                plans.Add(new(source, destination, reason, video));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or RegexMatchTimeoutException)
            { skipped.Add(new(source, "", "Skipped", ex.Message)); }
        }
        var collisions = plans.GroupBy(p => p.Destination, PathSafety.Comparer).Where(g => g.Count() > 1).ToArray();
        foreach (var group in collisions)
            foreach (var plan in group) { plans.Remove(plan); skipped.Add(new(plan.Source, plan.Destination, "Conflict", "Several sources produce the same destination; configure distinct titles")); }
        return new(plans.ToArray(), skipped.ToArray());
    }
}
