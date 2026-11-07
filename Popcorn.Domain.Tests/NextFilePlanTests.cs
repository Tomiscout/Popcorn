using NUnit.Framework;
using Popcorn.Application.Extensions;
using Popcorn.Domain.Models;

namespace Popcorn.Domain.Tests;

public class NextFilePlanTests
{
    private static MediaFile File(string path) => new() { Id = Guid.NewGuid(), ItemId = Guid.Empty, Path = path };

    private static MediaItem Item(params MediaFile[] files) => new()
    {
        Id = Guid.NewGuid(),
        Title = "Show",
        FolderDirectory = "/media/Show",
        Files = files.ToList(),
        History = [],
    };

    private static MediaFile Episode(int season, int episode) => File($"/media/Show/Show.S{season:00}E{episode:00}.1080p.x264-GRP.mkv");

    [Test]
    public void NextEpisode_NothingMissing()
    {
        var (first, second) = (Episode(1, 1), Episode(1, 2));
        var plan = Item(first, second).GetNextFilePlan(first.Id);
        Assert.That(plan.NextFileId, Is.EqualTo(second.Id));
        Assert.That(plan.NextLabel, Is.EqualTo("S01E02"));
        Assert.That(plan.MissingLabel, Is.Null);
    }

    [Test]
    public void PreviousEpisode_AcrossSeasonsAndGaps()
    {
        var (first, fourth, nextSeason) = (Episode(1, 1), Episode(1, 4), Episode(2, 1));
        var item = Item(nextSeason, fourth, first);
        Assert.That(item.GetNextFilePlan(first.Id).PreviousFileId, Is.Null);
        Assert.That(item.GetNextFilePlan(fourth.Id).PreviousFileId, Is.EqualTo(first.Id));
        var plan = item.GetNextFilePlan(nextSeason.Id);
        Assert.That(plan.PreviousFileId, Is.EqualTo(fourth.Id));
        Assert.That(plan.PreviousLabel, Is.EqualTo("S01E04"));
    }

    [Test]
    public void PreviousUnnumberedFile_FolderOrder()
    {
        var (a, b, nested) = (File("/media/Show/A.mkv"), File("/media/Show/B.mkv"), File("/media/Show/Extras/C.mkv"));
        var item = Item(nested, b, a);
        Assert.That(item.GetNextFilePlan(a.Id).PreviousFileId, Is.Null);
        Assert.That(item.GetNextFilePlan(b.Id).PreviousFileId, Is.EqualTo(a.Id));
        Assert.That(item.GetNextFilePlan(nested.Id).PreviousFileId, Is.EqualTo(b.Id));
    }

    [Test]
    public void GapInSeason_ReportsFirstMissingEpisode()
    {
        var (first, fourth) = (Episode(1, 1), Episode(1, 4));
        var plan = Item(first, fourth).GetNextFilePlan(first.Id);
        Assert.That(plan.NextFileId, Is.EqualTo(fourth.Id));
        Assert.That(plan.MissingLabel, Is.EqualTo("S01E02"));
    }

    [Test]
    public void NextSeasonStartsLate_ReportsItsFirstEpisode()
    {
        var (last, third) = (Episode(1, 10), Episode(2, 3));
        Assert.That(Item(last, third).GetNextFilePlan(last.Id).MissingLabel, Is.EqualTo("S02E01"));
    }

    [Test]
    public void NextSeasonFromItsFirstEpisode_NothingMissing()
    {
        var (last, first) = (Episode(1, 10), Episode(2, 1));
        Assert.That(Item(last, first).GetNextFilePlan(last.Id).MissingLabel, Is.Null);
    }

    [Test]
    public void WholeSeasonGap_ReportsSeason()
    {
        var (last, first) = (Episode(1, 10), Episode(3, 1));
        Assert.That(Item(last, first).GetNextFilePlan(last.Id).MissingLabel, Is.EqualTo("S02"));
    }

    [Test]
    public void OtherVersionOfSameEpisode_IsPassedOver()
    {
        var first = Episode(1, 1);
        var otherVersion = File("/media/Show/Show.S01E01.720p.x264-GRP.mkv");
        var second = Episode(1, 2);
        Assert.That(Item(first, otherVersion, second).GetNextFilePlan(first.Id).NextFileId, Is.EqualTo(second.Id));
    }

    [Test]
    public void LastEpisode_NothingNext()
    {
        var (first, last) = (Episode(1, 1), Episode(1, 2));
        Assert.That(Item(first, last).GetNextFilePlan(last.Id).NextFileId, Is.Null);
    }

    [Test]
    public void Specials_NoMissingWarning()
    {
        var (first, third) = (Episode(0, 1), Episode(0, 3));
        var plan = Item(first, third).GetNextFilePlan(first.Id);
        Assert.That(plan.NextFileId, Is.EqualTo(third.Id));
        Assert.That(plan.MissingLabel, Is.Null);
    }

    [TestCase("Breaking Bad", "Breaking Bad")]
    [TestCase("Show.S01.1080p.WEB-DL.x264-GRP", "Show")]
    public void PlayerTitle_DropsReleaseTags(string title, string expected)
    {
        Assert.That(new MediaItem { Title = title, ShortTitle = title }.GetPlayerTitle(), Is.EqualTo(expected));
    }

    [TestCase(1200.0, 2580.0, false)] // left halfway: resume it
    [TestCase(2500.0, 2580.0, true)] // in the credits: the next one
    [TestCase(null, null, true)] // position never reported (external player): the next one
    public void ContinueFile_ResumesUnfinishedElseNext(double? position, double? duration, bool expectNext)
    {
        var (first, second) = (Episode(1, 1), Episode(1, 2));
        first.PlaybackPosition = position;
        first.Duration = duration;
        var item = Item(first, second);
        item.History = [new ItemHistory { ItemId = item.Id, FileId = first.Id }];

        Assert.That(item.GetContinueFile()?.Id, Is.EqualTo(expectNext ? second.Id : first.Id));
    }

    [Test]
    public void ContinueFile_LastEpisodeFinished_StaysOnIt()
    {
        var last = Episode(1, 2);
        last.PlaybackPosition = last.Duration = 2580;
        var item = Item(Episode(1, 1), last);
        item.History = [new ItemHistory { ItemId = item.Id, FileId = last.Id }];

        Assert.That(item.GetContinueFile()?.Id, Is.EqualTo(last.Id));
    }

    [Test]
    public void NextEpisode_HasEpisodeTitle()
    {
        var first = Episode(1, 1);
        var second = File("/media/Show/Show.S01E02.The.Return.1080p.x264-GRP.mkv");
        var plan = Item(first, second).GetNextFilePlan(first.Id);
        Assert.That(plan.NextLabel, Is.EqualTo("S01E02"));
        Assert.That(plan.NextEpisodeTitle, Is.EqualTo("The Return"));
        Assert.That(plan.NextTitle, Is.Null);
    }

    [Test]
    public void NextUnnumberedFile_TitleWithoutItemTitle()
    {
        var first = File("/media/Show/Show Pilot.mkv");
        var second = File("/media/Show/Show - Behind the scenes.mkv");
        var plan = Item(first, second).GetNextFilePlan(first.Id);
        Assert.That(plan.NextFileId, Is.EqualTo(second.Id));
        Assert.That(plan.NextLabel, Is.Null);
        Assert.That(plan.NextTitle, Is.EqualTo("Behind the scenes"));
    }
}
