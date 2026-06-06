using TMDbLib.Objects.Movies;
using TMDbLib.Objects.TvShows;

namespace Popcorn.Server.Models.Requests;

public class MediaSearchDetailsResponse
{
    public Movie? Movie { get; set; } = null;
    public TvShow? TvShow { get; set; } = null;
}