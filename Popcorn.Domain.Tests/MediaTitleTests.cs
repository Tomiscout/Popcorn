using NUnit.Framework;
using Popcorn.Application.Extensions;

namespace Popcorn.Domain.Tests;

public class MediaTitleTests
{
    [TestCase("Inception.2010.1080p.BluRay.x264-SPARKS", "Inception (2010)")]
    [TestCase("The.Legend.of.Korra.S04.1080p.BluRay.x265-RARBG", "The Legend of Korra")]
    [TestCase("Breaking Bad S01-S05 720p WEB-DL", "Breaking Bad")]
    [TestCase("Dune.Part.Two.2024.2160p.WEBRip.x265.10bit-GRP", "Dune Part Two (2024)")]
    [TestCase("Inception (2010)", "Inception (2010)")]
    [TestCase("1917", "1917")]
    [TestCase("  Family videos ", "Family videos")]
    public void cleans_release_names(string name, string expected) =>
        Assert.That(MediaTitle.FromName(name), Is.EqualTo(expected));
}
