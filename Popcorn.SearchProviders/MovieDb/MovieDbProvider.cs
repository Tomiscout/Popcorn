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
using Popcorn.SearchProviders.Exceptions;

namespace Popcorn.SearchProviders.MovieDb
{
    public static class MovieDbProvider
    {
        private const string _apiKeyBearer = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJiOGFmNmRkMjM1ZjBhMzRkZDg3ZDM2OTdhY2Q3OWIyOSIsInN1YiI6IjYzZGZiNTQ4Y2QyMDQ2MDA5ZDRmM2ZmOSIsInNjb3BlcyI6WyJhcGlfcmVhZCJdLCJ2ZXJzaW9uIjoxfQ.-tjdZE__JgXNY6Z1Y50GtgQ5_4D23SSzPca-y_gw3DQ";
        private static IApiMovieRequest _movieApi;
        private static IApiTVShowRequest _tvShowApi;
        private static IApiConfigurationRequest _configApi;
        private static IApiGenreRequest _genreApi;
        private static ApiConfiguration _config = null;
        private static List<Genre> _genreCache = null;

        public static void Initialize()
        {
            MovieDbFactory.RegisterSettings(_apiKeyBearer);
            _movieApi = MovieDbFactory.Create<IApiMovieRequest>().Value;
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
                var result = await MovieDbRequests.GetMultiSearchResponse(query);
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

        public static async Task<Movie> GetMovieDetails(int id)
        {
            var details = await _movieApi.FindByIdAsync(id);
            if(details is not null)
            {
                return details.Item;
            }
            return null;
        }

        public static async Task<TVShow> GetTvDetails(int id)
        {
            var tvDetails = await _tvShowApi.FindByIdAsync(id);
            if (tvDetails is not null)
            {
                return tvDetails.Item;
            }
            return null;
        }
    }
}
