using SidecarBridge.Core;
using Xunit;

namespace SidecarBridge.Tests;

public sealed class MatchingTests
{
    [Fact]
    public void YoujoRegularAndSpecialsNeverMix()
    {
        var videos = Enumerable.Range(1, 12).Select(i => $"Youjo Senki TV {i:00} серия.mkv")
            .Concat(Enumerable.Range(0, 13).Select(i => $"Youjo Senki TV Sp {i:00} серия.mkv")).ToArray();
        foreach (var video in videos)
            Assert.Equal(video, Matching.Match(Path.GetFileNameWithoutExtension(video), videos).Video);
        Assert.NotEqual(Matching.ParseEpisode("Youjo Senki TV 01 серия"), Matching.ParseEpisode("Youjo Senki TV Sp 01 серия"));
        Assert.Equal(0, Matching.ParseEpisode("Youjo Senki TV Sp 00 серия")!.Episode);
    }
    [Theory]
    [InlineData("Show S01E02", "Show 1x02.mkv")]
    [InlineData("Show Episode 02", "Show 02 серия.mkv")]
    [InlineData("Show - 02", "Show Episode 02.mkv")]
    [InlineData("Шоу_02_серия", "шоу 02 серия.mkv")]
    [InlineData("Cafe\u0301 01", "Café 01.mkv")]
    public void SafeEquivalentNames(string source, string video) => Assert.Equal(video, Matching.Match(source, [video]).Video);
    [Theory]
    [InlineData("Show S01E02", "Show S02E02.mkv")]
    [InlineData("Show 02 серия", "Another Show 02 серия.mkv")]
    [InlineData("Show Sp 01", "Show TV 01.mkv")]
    [InlineData("Show S01E01 Director Cut", "Show S01E01 Theatrical.mkv")]
    [InlineData("[A] Show 01 [1080p]", "[B] Show 01 [720p].mkv")]
    [InlineData("Film 2026", "Film 26.mkv")]
    public void UnsafeMatchesAreRejected(string source, string video) => Assert.Null(Matching.Match(source, [video]).Video);
    [Theory]
    [InlineData("Film 2026")]
    [InlineData("Film 1080p")]
    [InlineData("Film x265")]
    [InlineData("Film [ABCD1234]")]
    [InlineData("Film 01-12")]
    [InlineData("Film 12 of 24")]
    public void NonEpisodeNumbersAreNotEpisodes(string name) => Assert.Null(Matching.ParseEpisode(name));
    [Fact]
    public void MultipleEditionsAndCaseVariantsRemainAmbiguous()
    {
        Assert.Null(Matching.Match("Show Episode 02", ["Show 02 серия.mkv", "Show 02 серия.mp4"]).Video);
        Assert.Null(Matching.Match("show 01", ["Show 01.mkv", "SHOW 01.mp4"]).Video);
    }
    [Theory]
    [InlineData("NDA Studio", "NDA Studio")]
    [InlineData("Signs", "Signs")]
    [InlineData("Default Studio", "%44efault Studio")]
    [InlineData("Unforced Team", "Un%66orced Team")]
    [InlineData("English", "Studio English")]
    [InlineData("A.B", "A%2EB")]
    public void TitlesCannotBecomeFlagsOrLanguage(string title, string expected) => Assert.Equal(expected, TrackNaming.SafeTitle(title));
    [Theory]
    [InlineData("Rus Sound/NDA Studio", "NDA Studio", "rus")]
    [InlineData("English Audio/Team", "Team", "eng")]
    [InlineData("Subtitles/Signs", "Signs", null)]
    [InlineData("RUS Sound [Studio]", "Studio", "rus")]
    [InlineData("日本語 Audio/翻訳", "翻訳", "jpn")]
    public void DirectoryLanguageIsExplicit(string directory, string title, string? language)
    {
        var info = TrackNaming.FromDirectory(directory);
        Assert.Equal(title, info.Title); Assert.Equal(language, info.Language);
    }
    [Fact]
    public void FlagsComeFromExplicitFilenameTokens()
    {
        var parts = TrackNaming.Parse("Film.eng.default.forced.sdh.srt");
        Assert.Equal("Film", parts.Stem); Assert.Equal("eng", parts.Language);
        Assert.True(parts.Default && parts.Forced && parts.HearingImpaired);
        Assert.False(TrackNaming.Parse("Film.Signs.ass").Forced);
    }
    [Theory]
    [InlineData("Film.en.hi.srt", "eng", true)]
    [InlineData("Film.hi.en.srt", "eng", true)]
    [InlineData("Film.hi.srt", "hin", false)]
    [InlineData("Film.und.srt", null, false)]
    public void HindiAndUndefinedLanguageFollowJellyfinRules(string file, string? language, bool hearing)
    {
        var parts = TrackNaming.Parse(file); Assert.Equal("Film", parts.Stem);
        Assert.Equal(language, parts.Language); Assert.Equal(hearing, parts.HearingImpaired);
    }
}
