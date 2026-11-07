using FluentAssertions;
using NUnit.Framework;

namespace Popcorn.FilenameParser.Tests
{
    [TestFixture]
    public class MediaNameParserFixture
    {
        [TestCase("/m/Inception (2010)/Inception.2010.1080p.BluRay.x264-SPARKS.mkv", "Inception", 2010, 1080, "H.264", "SPARKS")]
        [TestCase("/m/Dune.Part.Two.2024.2160p.WEBRip.x265.10bit-GRP.mp4", "Dune Part Two", 2024, 2160, "H.265", "GRP")]
        [TestCase("/m/2001 A Space Odyssey 1968 720p.mkv", "2001 A Space Odyssey", 1968, 720, null, null)]
        [TestCase("/d/vacation clip 2019.mp4", "vacation clip", 2019, null, null, null)]
        public void should_parse_movies(string path, string title, int year, int? resolution, string codec, string group)
        {
            var result = MediaNameParser.Parse(path);

            result.IsTv.Should().BeFalse();
            result.Title.Should().Be(title);
            result.Year.Should().Be(year);
            result.Seasons.Should().BeEmpty();
            result.Episodes.Should().BeEmpty();
            result.Resolution.Should().Be(resolution);
            result.VideoCodec.Should().Be(codec);
            result.ReleaseGroup.Should().Be(group);
        }

        [TestCase("/t/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.WEB-DL.H.264-NTb.mkv", "Breaking Bad", new[] { 1 }, new[] { 2 })]
        [TestCase("/t/Doctor Who (2005)/Season 1/Doctor.Who.2005.S01E02.720p.mkv", "Doctor Who 2005", new[] { 1 }, new[] { 2 })]
        [TestCase("/t/Show/Season 2/Show - 2x05 - Title.avi", "Show", new[] { 2 }, new[] { 5 })]
        [TestCase("/t/Show/Show S01E01E02 1080p.mkv", "Show", new[] { 1 }, new[] { 1, 2 })]
        [TestCase("/t/The.Legend.of.Korra.S04.1080p.BluRay.x265-RARBG/The.Legend.of.Korra.S04E05.1080p.BluRay.x265-RARBG.mp4", "The Legend of Korra", new[] { 4 }, new[] { 5 })]
        [TestCase("/t/Chernobyl (2019)/Chernobyl.Part.1.1080p.mkv", "Chernobyl", new[] { 1 }, new[] { 1 })]
        public void should_parse_episodes(string path, string title, int[] seasons, int[] episodes)
        {
            var result = MediaNameParser.Parse(path);

            result.IsTv.Should().BeTrue();
            result.Title.Should().Be(title);
            result.Seasons.Should().Equal(seasons);
            result.Episodes.Should().Equal(episodes);
        }

        [TestCase("/t/The.Umbrella.Academy.S01E08.Hello.World.1080p.NF.WEB-DL.DDP5.1.x264-NTb.mkv", "Hello World")]
        [TestCase("/t/Show/Season 2/Show - 2x05 - Title.avi", "Title")]
        [TestCase("/t/Show/Season 02/03 - Episode Title.mkv", "Episode Title")]
        [TestCase("/t/Show/Show S01E01E02 - Pilot (1) [1080p].mkv", "Pilot")]
        [TestCase("/t/Breaking Bad/Season 1/Breaking.Bad.S01E02.720p.WEB-DL.H.264-NTb.mkv", null)]
        [TestCase("/t/Show/Show S01E01E02 1080p.mkv", null)]
        [TestCase("/a/[SubsPlease] Frieren - 05 (1080p) [ABCD1234].mkv", null)]
        public void should_parse_episode_title(string path, string episodeTitle)
        {
            MediaNameParser.Parse(path).EpisodeTitle.Should().Be(episodeTitle);
        }

        [TestCase("/t/Avatar.Seven.Havens.S01E01.1080p.WEB.h264-GRACE[EZTVx.to].mkv", "Avatar Seven Havens S01E01", "WEB")]
        [TestCase("/t/The.Umbrella.Academy.S01E08.Hello.World.1080p.NF.WEB-DL.DDP5.1.x264-NTb.mkv", "The Umbrella Academy S01E08 Hello World", "WEB-DL")]
        [TestCase("/t/Show/Show S01E01E02 - Pilot (1) [1080p].mkv", "Show S01E01E02 - Pilot", null)]
        [TestCase("/a/[SubsPlease] Frieren - 05 (1080p) [ABCD1234].mkv", "Frieren - 05", null)]
        [TestCase("/t/Show/Season 02/03 - Episode Title.mkv", "03 - Episode Title", null)]
        [TestCase("/m/Inception (2010)/Inception.2010.1080p.BluRay.x264-SPARKS.mkv", "Inception 2010", "BluRay")]
        [TestCase("/m/Movie (2010) 1080p WEBRip.mkv", "Movie (2010)", "WEBRip")]
        [TestCase("/m/2001 A Space Odyssey 1968 720p.mkv", "2001 A Space Odyssey 1968", null)]
        [TestCase("/m/The.Proper.Way.2019.WEB.mkv", "The Proper Way 2019", "WEB")]
        [TestCase("/d/vacation clip.mp4", "vacation clip", null)]
        public void should_parse_clean_name_and_source(string path, string cleanName, string source)
        {
            var result = MediaNameParser.Parse(path);
            result.CleanName.Should().Be(cleanName);
            result.Source.Should().Be(source);
        }

        [Test]
        public void should_take_season_from_folder()
        {
            var result = MediaNameParser.Parse("/t/Show/Season 02/03 - Episode Title.mkv");

            result.IsTv.Should().BeTrue();
            result.Seasons.Should().Equal(2);
            result.Episodes.Should().Equal(3);
        }

        [Test]
        public void should_parse_absolute_anime_numbering_without_season()
        {
            var result = MediaNameParser.Parse("/a/[SubsPlease] Frieren - 05 (1080p) [ABCD1234].mkv");

            result.IsTv.Should().BeTrue();
            result.Title.Should().Be("Frieren");
            result.Seasons.Should().BeEmpty();
            result.Episodes.Should().Equal(5);
            result.Resolution.Should().Be(1080);
            result.ReleaseGroup.Should().Be("SubsPlease");
        }

        [Test]
        public void should_parse_daily_show_without_numbers()
        {
            var result = MediaNameParser.Parse("/t/The.Daily.Show.2024.03.05.1080p.WEB.h264-EDITH.mkv");

            result.IsTv.Should().BeTrue();
            result.Seasons.Should().BeEmpty();
            result.Episodes.Should().BeEmpty();
        }

        [TestCase("/m/Some Movie.mkv")]
        [TestCase("")]
        public void should_not_fail_on_names_without_tags(string path)
        {
            var result = MediaNameParser.Parse(path);

            result.IsTv.Should().BeFalse();
            result.Seasons.Should().BeEmpty();
            result.Episodes.Should().BeEmpty();
        }
    }
}
