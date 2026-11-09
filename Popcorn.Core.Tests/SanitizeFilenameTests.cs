using NUnit.Framework;

namespace Popcorn.Core.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
    }



    [Test]
    public void SanitizeFilename_1()
    {
        const string sut = "Sex.Education.S04E01.Episode.1.1080p.NF.WEB-DL.DDP5.1.H.264-Archie";

        Assert.Pass();
    }
}