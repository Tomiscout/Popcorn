using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;
using VideoBlazor.SearchProviders.Enum;
using VideoBlazor.SearchProviders.Exceptions;
using VideoBlazor.SearchProviders.Model;
using VideoBlazor.SearchProviders.Models;
using VideoBlazor.SearchProviders.Providers.Rarbg.DTO;
using VideoBlazor.SearchProviders.Providers.Rarbg.Exceptions;

namespace VideoBlazor.SearchProviders.Providers.Rarbg
{
    public class RarbgSearchProvider : ISearchProvider
    {
        //API Documentation https://torrentapi.org/apidocs_v2.txt
        private static readonly string _apiUrl = "http://torrentapi.org/pubapi_v2.php";
        private string _apiToken = null;

        public SearchSourceType Source => SearchSourceType.Rarbg;

        public async Task<SearchQueryResult> Search(string query)
        {
            try
            {
                var results = await AttemptSearch(query);
                return new SearchQueryResult()
                {
                    Source = Source,
                    Results = results,
                    Success = true
                };
            } catch(SearchFailedException e)
            {
                return new SearchQueryResult()
                {
                    Source = Source,
                    Success = false,
                    Error = e,
                    ErrorMessage = e.Message
                };
            }
        }

        private async Task<List<SearchResult>> AttemptSearch(string query, int attempts = 0)
        {
            string responseString = null;
            try
            {
                var response = await GetRarbgApiResult(query);
                responseString = await response.Content.ReadAsStringAsync();
            } catch(HttpRequestException e)
            {
                if (attempts > 3)
                {
                    throw new SearchFailedException("Failed to get Rarbg API result", e);
                }
                await Task.Delay(5000);
                return await AttemptSearch(query, ++attempts); //Recursion, but it's simple enough
            }

            try
            {
                var results = JsonSerializer.Deserialize<RarbgResponseWrapper>(responseString);
                if (results is null)
                {
                    var errorResult = JsonSerializer.Deserialize<RarbgSearchError>(responseString);
                    if (errorResult is null)
                    {
                        throw new RarbgException("Failed to parse Rarbg response");
                    }

                    throw errorResult.ErrorCode switch
                    {
                        2 or 4 => new RarbgTokenException(),
                        5 => new RarbgException("Rarbg rate limited! Try again later."),
                        _ => new RarbgException("Unknown error code"),
                    };
                }

                return results.Results.Select(result => ParseResult(result)).ToList();
            }
            catch (RarbgTokenException e)
            {
                if (attempts > 3)
                {
                    throw new SearchFailedException("Failed to get Rarbg API token", e);
                }
                await Task.Delay(5000);
                await Login();
                return await AttemptSearch(query, ++attempts); //Recursion, but it's simple enough
            }
            catch (Exception e)
            {
                throw new SearchFailedException("Failed to parse search result", e);
            }
        }

        private async Task<HttpResponseMessage> GetRarbgApiResult(string query, int attempts = 0)
        {
            if(_apiToken is null)
            {
                await Login();
            }
            using HttpClient client = new HttpClient();
            var parameters = new Dictionary<string, string>
            {
                { "mode", "search" },
                { "search_string", query },
                { "format", "json_extended" },
                { "app_id", "VideoBlazor" },
                { "token", _apiToken }
            };
            try
            {
                return await client.GetAsync(new Uri(QueryHelpers.AddQueryString(_apiUrl, parameters)));
            }
            catch (Exception e)
            {
                throw new HttpRequestException("Failed to call Rarbg API", e);
            }
        }

        //TODO this could be automapper
        private SearchResult ParseResult(RarbgSearchResult result)
        {
            return new SearchResult()
            {
                Source = Source,
                Title = result.Title,
                Seeds = result.Seeds,
                Peers = result.Peers,
                MagnetLink = result.MagnetLink,
                UploadedDate = DateTime.Parse(result.UploadDate),
                Size = result.Size,
                Imdb = result.EpisodeInfo.Imdb,
                Category = result.Category,
                TorrentPageUrl = result.TorrentPageUrl
            };
        }

        private async Task Login()
        {
            using HttpClient client = new HttpClient();
            var parameters = new Dictionary<string, string>
            {
                { "get_token", "get_token" },
                { "format", "json" },
                { "app_id", "VideoBlazor" },
            };
            try
            {
                var content = await client.GetStringAsync(new Uri(QueryHelpers.AddQueryString(_apiUrl, parameters)));
                var loginResult = JsonSerializer.Deserialize<RarbgLoginResult>(content);
                if(loginResult is null)
                {
                    throw new RarbgTokenException("Failed to parse Rargb login response");
                }
                _apiToken = loginResult.Token;
            }
            catch(RarbgTokenException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new HttpRequestException("Failed to get Rarbg API token", e);
            }
        }
    }
}
