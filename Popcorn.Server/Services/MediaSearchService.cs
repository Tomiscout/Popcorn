using Popcorn.SearchProviders.Imdb;
using Popcorn.SearchProviders.MovieDb;
using Popcorn.Server.Attributes;
using Popcorn.Server.Models;
using Popcorn.Server.Models.Requests;
using TMDbLib.Objects.General;

namespace Popcorn.Server.Services;

[WebSocketService]
public class MediaSearchService(WebSocketBus bus, ILogger<MediaSearchService> logger, MovieDbProvider movieDbProvider, ImdbScoreService imdbScoreService)
{

    [WebSocketEvent(SocketRequests.MediaSearch)]
    public async Task HandleMediaSearch(MediaSearchRequest request)
    {
        var queryString = request.SearchText.Trim();
        if (string.IsNullOrWhiteSpace(queryString)) return;
        
        List<MovieDbSearchResult>? results = null;
        try
        {
            results = await movieDbProvider.Search(queryString);
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
            if (request.Type == MediaType.Tv)
            {
                response.TvShow = await movieDbProvider.GetTvDetails(request.Id);
            }
            else if(request.Type == MediaType.Movie)
            {
                response.Movie = await movieDbProvider.GetMovieDetails(request.Id);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch {type} details for {id}", request.Type, request.Id);
        }
        
        await bus.SendEventAsync(SocketRequests.MediaSearchDetails, response);

        if (request.Type == MediaType.Movie)
        {
            var imdbId = response.Movie?.ImdbId;
            if (!string.IsNullOrWhiteSpace(imdbId))
            {
                var imdbScore = await imdbScoreService.GetScoreAsync(imdbId);
                await bus.SendEventAsync(SocketRequests.MediaSearchDetailsImdb, new MediaSearchDetailsImdScoreResult
                {
                    Id = request.Id,
                    Score = imdbScore
                });
            }
        }
    }  
}