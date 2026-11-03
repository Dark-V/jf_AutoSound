using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SidecarBridge.Core;

public static class Matching
{
    public static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    public static string Normalize(string text) => Regex.Replace(text.Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[\s._]+", " ", RegexOptions.CultureInvariant, RegexTimeout).Trim();
    private static Match EpisodeMatch(string text, string pattern) => Regex.Match(Normalize(text), pattern, RegexOptions.CultureInvariant, RegexTimeout);
    public sealed record EpisodeKey(string Work, int? Season, string Type, int Episode, string Edition);
    public static EpisodeKey? ParseEpisode(string name)
    {
        var match = EpisodeMatch(name, @"^(?<work>.+?)\s*(?:s(?<season>\d{1,2})e(?<episode>\d{1,3})|(?<season>\d{1,2})x(?<episode>\d{1,3}))\b(?<tail>.*)$");
        if (!match.Success)
            match = EpisodeMatch(name, @"^(?<work>.+?)\s+(?:(?<type>tv|sp|special|ova|oad|ona)\s+)?(?:(?:episode|ep)\s+)?(?<episode>\d{1,3})\b(?:\s*(?:серия|episode)\b)?(?<tail>.*)$");
        if (!match.Success) return null;
        var tail = match.Groups["tail"].Value.Trim();
        // A range/total, resolution, codec or hash is not an episode number.
        if (Regex.IsMatch(tail, @"^(?:p\b|bit\b|\s*(?:-|/|of|из)\s*\d)", RegexOptions.CultureInvariant, RegexTimeout)) return null;
        var work = match.Groups["work"].Value.Trim().TrimEnd('-').Trim();
        if (string.IsNullOrWhiteSpace(work)) return null;
        var type = match.Groups["type"].Value;
        type = type switch { "" or "tv" => "tv", "special" => "sp", _ => type };
        return new(work, match.Groups["season"].Success ? int.Parse(match.Groups["season"].Value, CultureInfo.InvariantCulture) : null,
            type, int.Parse(match.Groups["episode"].Value, CultureInfo.InvariantCulture), tail);
    }
    public static (string? Video, string Reason) Match(string sourceStem, IEnumerable<string> videos)
        => new VideoIndex(videos).Match(sourceStem);
    public sealed class VideoIndex
    {
        private readonly Dictionary<string, string[]> _exact;
        private readonly Dictionary<string, string[]> _normal;
        private readonly Dictionary<EpisodeKey, string[]> _episodes;
        public VideoIndex(IEnumerable<string> videos)
        {
            var names = videos.Select(v => (Path: v, Stem: Path.GetFileNameWithoutExtension(v))).ToArray();
            _exact = names.GroupBy(v => v.Stem, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(v => v.Path).ToArray(), StringComparer.Ordinal);
            _normal = names.GroupBy(v => Normalize(v.Stem), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(v => v.Path).ToArray(), StringComparer.Ordinal);
            _episodes = names.Select(v => (v.Path, Key: ParseEpisode(v.Stem))).Where(v => v.Key is not null).GroupBy(v => v.Key!).ToDictionary(g => g.Key, g => g.Select(v => v.Path).ToArray());
        }
        public (string? Video, string Reason) Match(string sourceStem)
        {
            if (_exact.TryGetValue(sourceStem, out var exact)) return Unique(exact, "Exact basename");
            if (_normal.TryGetValue(Normalize(sourceStem), out var normal)) return Unique(normal, "Normalized basename");
            var key = ParseEpisode(sourceStem);
            if (key is null) return (null, "No safe basename or episode match; configure an explicit mapping");
            return Unique(_episodes.GetValueOrDefault(key) ?? [], "Same work, season, episode type/number and edition");
        }
    }
    private static (string? Video, string Reason) Unique(string[] paths, string reason) => paths.Length switch
    {
        1 => (paths[0], reason), 0 => (null, "No matching work, season, episode type/number and edition"),
        _ => (null, "Ambiguous: several video files match; nothing linked")
    };
}
