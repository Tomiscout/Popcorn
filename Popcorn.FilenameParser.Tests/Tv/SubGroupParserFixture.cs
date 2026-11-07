using FluentAssertions;
using NUnit.Framework;

namespace Popcorn.FilenameParser.Tests.Tv
{
    [TestFixture]
    public class SubGroupParserFixture
    {
        [TestCase("[GHOST][1080p] Series - 25 [BD HEVC 10bit Dual Audio AC3][AE0ADDBA]", "GHOST")]
        public void should_parse_sub_group_from_title_as_release_group(string title, string expected)
        {
            var result = EpisodeParser.ParseTitle(title);

            result.Should().NotBeNull();
            result.ReleaseGroup.Should().Be(expected);
        }
    }
}
