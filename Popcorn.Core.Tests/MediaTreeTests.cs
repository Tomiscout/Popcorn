using NUnit.Framework;
using Popcorn.Application.Extensions;
using Popcorn.Domain;
using Popcorn.Domain.Enum;
using Popcorn.Domain.Models;

namespace Popcorn.Core.Tests;

/// <summary>Items fed by several sources (a download attached, items merged): one virtual folder per source.</summary>
public class MediaTreeTests
{
    private static readonly ItemSource Library = new() { Title = "Show", RootPath = "/media/Show", AddedAt = new DateTime(2026, 1, 1) };
    private static readonly ItemSource Download = new() { Title = "Show.S02.1080p.WEB-GRP", RootPath = "/downloads/Show.S02.1080p.WEB-GRP", AddedAt = new DateTime(2026, 2, 1) };

    private static MediaFile File(string path, ItemSource source, FileType type = FileType.File) =>
        new() { Id = Guid.NewGuid(), Path = path, Title = Path.GetFileName(path), Type = type, SourceId = source.Id };

    private static MediaItem Item(List<ItemSource> sources, params MediaFile[] files)
    {
        var item = new MediaItem { Id = Guid.NewGuid(), Title = "Show", FolderDirectory = "/media/Show", Files = files.ToList(), History = [], Sources = sources };
        MediaTree.Build(item);
        return item;
    }

    [Test]
    public void OneSource_TreeIsTheDisk()
    {
        var episode = File("/media/Show/Season 1/Show.S01E01.mkv", Library);
        var season = File("/media/Show/Season 1", Library, FileType.Folder);
        var item = Item([Library], episode, season);
        Assert.That(episode.Tree(), Is.EqualTo(episode.Path));
        Assert.That(item.Files.GetFilesForDirectory("/media/Show").Select(f => f.Id), Is.EqualTo(new[] { season.Id }));
    }

    [Test]
    public void TwoSources_VirtualFolderPerSource()
    {
        var old = File("/media/Show/Show.S01E01.mkv", Library);
        var downloaded = File("/downloads/Show.S02.1080p.WEB-GRP/Show.S02E01.mkv", Download);
        var downloadRoot = File("/downloads/Show.S02.1080p.WEB-GRP", Download, FileType.Folder);
        var item = Item([Library, Download], old, downloaded, downloadRoot);

        var root = item.Files.GetFilesForDirectory("/media/Show");
        Assert.That(root.All(f => f.IsVirtual), Is.True);
        Assert.That(root.Select(f => f.Title), Is.EquivalentTo(new[] { "Show", "Show.S02.1080p.WEB-GRP" }));
        Assert.That(downloadRoot.IsHidden(), Is.True, "a source's own folder is its virtual folder already");

        var downloadFolder = root.Single(f => f.Id == Download.Id);
        Assert.That(item.Files.GetFilesForDirectory(downloadFolder.Tree()).Single().Id, Is.EqualTo(downloaded.Id));
        Assert.That(item.BuildNavigationPath(downloadFolder.Tree()).Single().FileId, Is.EqualTo(Download.Id));
    }

    [Test]
    public void TwoSources_NextEpisodeCrossesVirtualFolders()
    {
        var first = File("/media/Show/Show.S01E01.mkv", Library);
        var second = File("/downloads/Show.S02.1080p.WEB-GRP/Show.S01E02.mkv", Download);
        var item = Item([Library, Download], first, second);
        Assert.That(item.GetNextFilePlan(first.Id).NextFileId, Is.EqualTo(second.Id));
    }

    [Test]
    public void Matcher_ReleaseNameFindsItsShow()
    {
        var show = new MediaItem { Id = Guid.NewGuid(), Title = "The Expanse", FolderDirectory = "/media/The Expanse" };
        var other = new MediaItem { Id = Guid.NewGuid(), Title = "The Office", FolderDirectory = "/media/The Office" };
        var movie = new MediaItem { Id = Guid.NewGuid(), Title = "Dune (2021)", FolderDirectory = "/media/Dune" };

        var ranked = MediaItemMatcher.Rank("The.Expanse.S02.1080p.BluRay.x264-GRP", [other, movie, show]);
        Assert.That(ranked[0].Item, Is.SameAs(show));
        Assert.That(ranked[0].Score, Is.EqualTo(1));
        Assert.That(MediaItemMatcher.Rank("Dune.2021.2160p.WEB-DL.x265-GRP", [other, movie, show])[0].Item, Is.SameAs(movie));
    }
}
