using Popcorn.SearchProviders.MovieDb;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaSearchService(WebSocketBus bus, ILogger<MediaSearchService> logger)
{

    [WebSocketEvent(SocketRequests.MediaSearch)]
    public async Task HandleMediaSearch(MediaSearchRequest request)
    {
        var queryString = request.SearchText.Trim();
        if (string.IsNullOrWhiteSpace(queryString)) return;
        
        List<MovieDbSearchResult>? results = null;
        try
        {
            results = await MovieDbProvider.Search(queryString);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch MovieDb for query:{query}", request.SearchText);
        }

        await bus.SendEventAsync(SocketRequests.MediaSearch, results ?? []);
    }   
    
    [WebSocketEvent(SocketRequests.MediaSearchDetails)]
    public async Task HandleMediaSearchDetails(MediaSearchDetailsRequest request)
    {
        var response = new MediaSearchDetailsResponse();
        try
        {
            if (request.Type == MovieDbMediaType.TvShow)
            {
                response.TvShow = await MovieDbProvider.GetTvDetails(request.Id);
            }
            else
            {
                response.Movie = await MovieDbProvider.GetMovieDetails(request.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch {type} details for {id}", request.Type, request.Id);
        }
       
        
        await bus.SendEventAsync(SocketRequests.MediaSearchDetails, response);
    }  
}