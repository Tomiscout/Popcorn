using Popcorn.SearchProviders.MovieDb;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaSearchService(WebSocketBus bus)
{

    [WebSocketEvent(SocketRequests.MediaSearch)]
    public async Task HandleMediaSearch(MediaSearchRequest request)
    {
        var queryString = request.SearchText.Trim();
        if (string.IsNullOrWhiteSpace(queryString)) return;
        
        var response = await MovieDbProvider.Search(queryString);
        await bus.SendEventAsync(SocketRequests.MediaSearch, response);
    }   
    
    [WebSocketEvent(SocketRequests.MediaSearchDetails)]
    public async Task HandleMediaSearchDetails(MediaSearchDetailsRequest request)
    {
        if (request.Type == MovieDbMediaType.TvShow)
        {
            await MovieDbProvider.GetTvDetails(request.Id);
        }
        else
        {
            await MovieDbProvider.GetMovieDetails(request.Id);
        }
        var response = await MovieDbProvider.Search(queryString);
        await bus.SendEventAsync(SocketRequests.MediaSearch, response);
    }  
}