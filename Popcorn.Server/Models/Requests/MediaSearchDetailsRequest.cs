using Popcorn.SearchProviders.MovieDb;

namespace Popcorn.Server.Models.Requests;

public class MediaSearchDetailsRequest
{
    public int Id { get; set; }
    public MovieDbMediaType Type { get; set; }
}