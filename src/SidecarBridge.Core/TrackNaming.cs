using System.Globalization;
using System.Text.RegularExpressions;

namespace SidecarBridge.Core;

public static class TrackNaming
{
    private static readonly Dictionary<string, string> Languages = BuildLanguages();
    private static Dictionary<string, string> BuildLanguages()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures).Where(c => c.Name.Length > 0))
        {
            foreach (var key in new[] { culture.Name, culture.TwoLetterISOLanguageName, culture.ThreeLetterISOLanguageName, culture.EnglishName, culture.NativeName })
                result.TryAdd(key, culture.ThreeLetterISOLanguageName);
        }
        result["Рус"] = result["Русский"] = result["RUS"] = "rus";
        result["jpn"] = "jpn";
        result["ger"] = "deu"; result["fre"] = "fra"; result["chi"] = "zho";
        return result;
    }
    public static string? Language(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "und") return null;
        if (Languages.TryGetValue(value, out var language)) return language;
        try
        {
            var culture = CultureInfo.GetCultureInfo(value);
            if (!culture.IsNeutralCulture && culture.Name.Contains('-') && Languages.ContainsKey(culture.TwoLetterISOLanguageName)) return culture.Name;
        }
        catch (CultureNotFoundException) { }
        return null;
    }
    public sealed record NameParts(string Stem, string? Language, bool Default, bool Forced, bool HearingImpaired);
    public static NameParts Parse(string source) => ParseTokens(Path.GetFileNameWithoutExtension(source).Split('.'), 1).Parts;

    public static (NameParts Parts, string Title) ParseSuffix(string suffix) => ParseTokens(suffix.Split('.'), 0);

    private static (NameParts Parts, string Title) ParseTokens(string[] tokens, int minimumStemParts)
    {
        string? language = null;
        bool isDefault = false, forced = false, hearing = false;
        var title = new List<string>();
        var i = tokens.Length - 1;
        for (; i >= minimumStemParts; i--)
        {
            var last = tokens[i];
            if (last.Equals("default", StringComparison.OrdinalIgnoreCase)) isDefault = true;
            else if (last.Equals("forced", StringComparison.OrdinalIgnoreCase) || last.Equals("foreign", StringComparison.OrdinalIgnoreCase)) forced = true;
            else if (last.Equals("sdh", StringComparison.OrdinalIgnoreCase) || last.Equals("cc", StringComparison.OrdinalIgnoreCase)) hearing = true;
            else if (last.Equals("hi", StringComparison.OrdinalIgnoreCase) && language is not null) hearing = true;
            else if (Language(last) is { } other && language == "hin" && other != "hin") { language = other; hearing = true; }
            else if (Language(last) is { } known && (language is null || minimumStemParts == 0)) language ??= known;
            else if (!last.Equals("und", StringComparison.OrdinalIgnoreCase))
            {
                if (minimumStemParts != 0) break;
                if (!string.IsNullOrWhiteSpace(last)) title.Add(last.Trim());
            }
        }
        title.Reverse();
        return (new(string.Join('.', tokens.Take(i + 1)), language, isDefault, forced, hearing), string.Join('.', title));
    }
    public static bool IsTrackDirectory(string relative) => relative.Split('/').Any(part =>
        Regex.IsMatch(part, @"\b(audio|sound|subs?|subtitles|озвучк\p{L}*|субтитры)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Matching.RegexTimeout));
    public static (string Title, string? Language) FromDirectory(string relative)
    {
        var parts = relative.Split('/');
        string? language = null;
        foreach (var part in parts)
        {
            var prefix = Regex.Split(part, @"\s+(?:audio|sound|subs?|subtitles)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Matching.RegexTimeout)[0];
            language ??= Language(prefix.Trim());
        }
        var title = parts[^1];
        var bracket = Regex.Match(title, @"\[([^\]]+)\]\s*$", RegexOptions.CultureInvariant, Matching.RegexTimeout);
        if (bracket.Success) title = bracket.Groups[1].Value;
        else if (Regex.IsMatch(title, @"\b(audio|sound|subs?|subtitles)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Matching.RegexTimeout)) title = "";
        else if (Language(title) is not null) title = "";
        return (title, language);
    }
    public static string SafeTitle(string title)
    {
        // 12.4 tests default/forced/foreign by SUBSTRING, not token equality.
        // Percent-encode one letter, reversibly, to prevent a studio triggering a flag.
        var safe = string.Concat(title.Normalize().Select(c => char.IsControl(c) || "<>:\"/\\|?*.%".Contains(c) ? $"%{(int)c:X2}" : c.ToString())).Trim().TrimEnd('.');
        safe = Regex.Replace(safe, "default|forced|foreign", m => $"%{(int)m.Value[0]:X2}" + m.Value[1..], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Matching.RegexTimeout);
        if (Language(safe) is not null || safe.Equals("sdh", StringComparison.OrdinalIgnoreCase) || safe.Equals("cc", StringComparison.OrdinalIgnoreCase)) safe = "Studio " + safe;
        return safe;
    }
    public static string Destination(string video, string source, string title, string? language, bool isDefault, bool forced, bool hearing, bool languageFirst = false)
    {
        var suffix = new List<string>();
        // Preserve the established language-first convention for labels from filenames.
        if (languageFirst && language is not null) suffix.Add(language == "rus" ? "ru" : language);
        if (!string.IsNullOrWhiteSpace(title)) suffix.Add(SafeTitle(title));
        if (!languageFirst && language is not null) suffix.Add(language);
        if (isDefault) suffix.Add("default");
        if (forced) suffix.Add("forced");
        if (hearing) suffix.Add("sdh");
        if (suffix.Count == 0) suffix.Add("External");
        return Path.Combine(Path.GetDirectoryName(video)!, Path.GetFileNameWithoutExtension(video) + "." + string.Join('.', suffix) + Path.GetExtension(source).ToLowerInvariant());
    }
}
