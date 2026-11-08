using NUnit.Framework;
using Popcorn.Application.Utils;

namespace Popcorn.Domain.Tests;

public class ExternalSubtitlesTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "popcorn-subs-" + Guid.NewGuid());
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, true);

    private string Touch(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private List<string> Found(string video, string? itemFolder = null) =>
        ExternalSubtitles.Find(video, itemFolder ?? _root).Select(s => Path.GetRelativePath(_root, s.Path)).ToList();

    [Test]
    public void SubsFolderNamedAsTheVideo()
    {
        var e1 = Touch("Show.S04E01.1080p.BluRay.x265-RARBG.mp4");
        Touch("Show.S04E02.1080p.BluRay.x265-RARBG.mp4");
        Touch("Subs/Show.S04E01.1080p.BluRay.x265-RARBG/3_English.srt");
        Touch("Subs/Show.S04E01.1080p.BluRay.x265-RARBG/4_English.srt");
        Touch("Subs/Show.S04E02.1080p.BluRay.x265-RARBG/3_English.srt");

        Assert.That(Found(e1), Is.EqualTo(new[]
        {
            "Subs/Show.S04E01.1080p.BluRay.x265-RARBG/3_English.srt",
            "Subs/Show.S04E01.1080p.BluRay.x265-RARBG/4_English.srt",
        }));
        var subtitle = ExternalSubtitles.Find(e1, _root)[0];
        Assert.That(subtitle.Language, Is.EqualTo("eng"));
        Assert.That(subtitle.Title, Is.EqualTo("3_English"));
    }

    [Test]
    public void SameNameWithLanguageSuffix()
    {
        var e1 = Touch("Show.S01E01.mkv");
        Touch("Show.S01E02.mkv");
        Touch("Show.S01E01.en.srt");
        Touch("Show.S01E01.ass");
        Touch("Show.S01E02.en.srt");

        Assert.That(Found(e1), Is.EqualTo(new[] { "Show.S01E01.ass", "Show.S01E01.en.srt" }));
        Assert.That(ExternalSubtitles.Find(e1, _root).Single(s => s.Path.EndsWith(".srt")).Language, Is.EqualTo("eng"));
    }

    [Test]
    public void OneVideoTakesEverySubtitle()
    {
        var movie = Touch("Movie.2010.1080p.mkv");
        Touch("Subs/English.srt");
        Touch("Subs/French.srt");

        Assert.That(Found(movie), Has.Count.EqualTo(2));
    }

    [Test]
    public void EpisodeNumbersMatch()
    {
        var e1 = Touch("Show.S01E01.1080p.WEB.mkv");
        Touch("Show.S01E02.1080p.WEB.mkv");
        Touch("Subs/Show S01E01 English.srt");
        Touch("Subs/Show S01E02 English.srt");

        Assert.That(Found(e1), Is.EqualTo(new[] { "Subs/Show S01E01 English.srt" }));
    }

    [Test]
    public void UnrelatedSubtitlesAreLeft()
    {
        var a = Touch("Movie.A.2010.mkv");
        Touch("Movie.B.2012.mkv");
        Touch("Movie.B.2012.srt");
        Touch("random.srt");

        Assert.That(Found(a), Is.Empty);
    }

    [Test]
    public void VobSubIsOneTrack()
    {
        var movie = Touch("Movie.2010.mkv");
        Touch("Movie.2010.idx");
        Touch("Movie.2010.sub");

        Assert.That(Found(movie), Is.EqualTo(new[] { "Movie.2010.idx" }));
    }

    [Test]
    public void WithoutItemFolderTheVideosFolderIsSearched()
    {
        var movie = Touch("Movie/Movie.2010.mkv");
        Touch("Movie/English.srt");
        Touch("Other/Other.srt");

        Assert.That(Found(movie, itemFolder: Path.Combine(_root, "Elsewhere")), Is.EqualTo(new[] { "Movie/English.srt" }));
    }
}
