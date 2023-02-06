using DM.MovieApi;
using DM.MovieApi.ApiResponse;
using DM.MovieApi.MovieDb;
using DM.MovieApi.MovieDb.Configuration;
using DM.MovieApi.MovieDb.Movies;
using DM.MovieApi.MovieDb.TV;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using DM.MovieApi.MovieDb.Genres;
using VideoBlazor.SearchProviders.Exceptions;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public static class MovieDbProvider
    {
        private const string _apiKeyBearer = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJiOGFmNmRkMjM1ZjBhMzRkZDg3ZDM2OTdhY2Q3OWIyOSIsInN1YiI6IjYzZGZiNTQ4Y2QyMDQ2MDA5ZDRmM2ZmOSIsInNjb3BlcyI6WyJhcGlfcmVhZCJdLCJ2ZXJzaW9uIjoxfQ.-tjdZE__JgXNY6Z1Y50GtgQ5_4D23SSzPca-y_gw3DQ";
        private const string _apiKey = "b8af6dd235f0a34dd87d3697acd79b29";
        private static IApiMovieRequest _movieApi;
        private static IApiTVShowRequest _tvShowApi;
        private static IApiConfigurationRequest _configApi;
        private static IApiGenreRequest _genreApi;
        private static ApiConfiguration _config = null;
        private static List<Genre> _genreCache = null;

        public static void Initialize()
        {
            MovieDbFactory.RegisterSettings(_apiKeyBearer);
            _tvShowApi = MovieDbFactory.Create<IApiTVShowRequest>().Value;
            _configApi = MovieDbFactory.Create<IApiConfigurationRequest>().Value;
            _genreApi = MovieDbFactory.Create<IApiGenreRequest>().Value;
        }

        public static async Task<List<MovieDbSearchResult>> Search(string query)
        {
            if (_config is null)
            {
                _config = (await _configApi.GetAsync()).Item;
            }
            if (_genreCache is null)
            {
                _genreCache = (await _genreApi.GetAllAsync()).Item.ToList() ?? GenreFactory.GetAll().ToList();
            }

            var results = new List<MovieDbSearchResult>();
            try
            {
                var result = await GetMultiSearchResponse(query);
                results.AddRange(result.Results.Select(r => new MovieDbSearchResult()
                {
                    MediaType = GetMediaType(r.MediaType),
                    Id = r.Id,
                    Title = r.Title ?? r.OriginalTitle ?? r.Name ?? r.OriginalName,
                    Description = r.Overview,
                    ReleaseDate = r.ReleaseDate ?? r.FirstAirDate,
                    PosterPath = string.IsNullOrWhiteSpace(r.PosterPath) ? null : $"{_config.Images.RootUrl}{_config.Images.Posters[2]}{r.PosterPath}",
                    BackdropPath = string.IsNullOrWhiteSpace(r.BackdropPath) ? null : $"{_config.Images.RootUrl}original{r.BackdropPath}",
                    ProfilePath = string.IsNullOrWhiteSpace(r.ProfilePath) ? null : $"{_config.Images.RootUrl}original{r.ProfilePath}",
                    Genres = GetGenresFromIds(r.GenreIds)
                }).ToList());
            }
            catch (Exception e)
            {
                throw new MovieDbException("Error getting MovieDb search results", e);
            }

            return results;
        }

        private static MovieDbMediaType GetMediaType(string mediaType) => mediaType switch
        {
            "movie" => MovieDbMediaType.Movie,
            "tv" => MovieDbMediaType.TvShow,
            "person" => MovieDbMediaType.Person,
            _ => MovieDbMediaType.Movie
        };

        private static List<string> GetGenresFromIds(IReadOnlyList<int> ids)
        {
            if(ids is null)
            {
                return new List<string>();
            }
            var genres = new List<string>();
            foreach(var id in ids)
            {
                var foundGenre = _genreCache.FirstOrDefault(g => g.Id == id);
                if (foundGenre is null)
                {
                    continue;
                }
                genres.Add(foundGenre.Name);
            }
            return genres;
        }

        public static async Task<string> GetImdbId(int movieDbId)
        {
            //TODO tv show fallback
            var details = await _movieApi.FindByIdAsync(movieDbId);
            return details.Item.ImdbId;
        }

        //This is separate, since no library suppoorts multi search
        public static async Task<MovieDbApiResponse> GetMultiSearchResponse(string query)
        {
            var param = new Dictionary<string, string>
            {
                {"language", "en"},
                {"query", query },
                {"api_key", _apiKey }
            };

            string command = $"search/multi";
            using HttpClient client = CreateClient();
            var cmd = CreateCommand(command, param);

            HttpResponseMessage response = await client.GetAsync(cmd);
            string json = await response.Content.ReadAsStringAsync();

            // rate limit will not exist if there is an error.
            if (response.IsSuccessStatusCode == false)
            {
                var error = new MovieDbApiResponse
                {
                    // This will throw up if the error is page number = 0; the resultant json will be: {"errors":["page must be greater than 0"]}
                    // in other words, the json will not include a status_code. Asked the api devs and this is a known issue they are working on.
                    // What to do? Nothing really, the page guard at the top of the method will keep the page number > 0.
                    Error = JsonSerializer.Deserialize<MovieDbApiError>(json),
                    CommandText = response.RequestMessage?.RequestUri?.ToString() ?? "response message/uri was null",
                    Json = json,
                };

                return error;
            }

            try
            {
                var result = JsonSerializer.Deserialize<MovieDbApiResponse>(json, new JsonSerializerOptions()
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    Converters = { new NullableConverterFactory() }
                });
                return result;
            } catch (Exception e)
            {
                var error = new MovieDbApiResponse
                {
                    // This will throw up if the error is page number = 0; the resultant json will be: {"errors":["page must be greater than 0"]}
                    // in other words, the json will not include a status_code. Asked the api devs and this is a known issue they are working on.
                    // What to do? Nothing really, the page guard at the top of the method will keep the page number > 0.
                    Error = new MovieDbApiError() { Message = e.Message, Exception = e},
                    CommandText = response.RequestMessage?.RequestUri?.ToString() ?? "response message/uri was null",
                    Json = json,
                };

                return error;
            }
        }

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseDefaultCredentials = true,
                AutomaticDecompression = DecompressionMethods.GZip,
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.BaseAddress = new Uri("http://api.themoviedb.org/3/");

            return client;
        }

        private static string CreateCommand(string rootCommand, IDictionary<string, string> parameters)
        {
            string tokens = parameters.Any()
                ? string.Join("&", parameters.Select(x => $"{x.Key}={WebUtility.UrlEncode(x.Value)}"))
                : string.Empty;

            if (string.IsNullOrWhiteSpace(tokens) == false)
            {
                rootCommand += $"?{tokens}";
            }

            return rootCommand;
        }
    }
}
