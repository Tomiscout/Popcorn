using Popcorn.SearchProviders.Imdb;

namespace Popcorn.Server.Models.Requests;

public class MediaSearchDetailsImdScoreResult
{
    public int Id { get; set; }
    public ImdbScore Score { get; set; }
}