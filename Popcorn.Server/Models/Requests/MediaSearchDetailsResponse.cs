using DM.MovieApi.MovieDb.Movies;
using DM.MovieApi.MovieDb.TV;

namespace Popcorn.Server.Models.Requests;

public class MediaSearchDetailsResponse
{
    public Movie? Movie { get; set; } = null;
    public TVShow? TvShow { get; set; } = null;
}