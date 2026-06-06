using Popcorn.SearchProviders.MovieDb;
using TMDbLib.Objects.General;

namespace Popcorn.Server.Models.Requests;

public class MediaSearchDetailsRequest
{
    public int Id { get; set; }
    public MediaType Type { get; set; }
}