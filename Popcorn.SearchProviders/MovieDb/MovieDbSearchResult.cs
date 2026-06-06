using Popcorn.SearchProviders.Omdb;
using TMDbLib.Objects.General;

namespace Popcorn.SearchProviders.MovieDb
{
    public class MovieDbSearchResult
    {
        public MediaType MediaType { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string PosterPath { get; set; }
        public string BackdropPath { get; set; }
        public string ProfilePath { get; set; }
        public List<string> Genres { get; set; }
        // public OmdbResult Ratings { get; set; }
    }
}
