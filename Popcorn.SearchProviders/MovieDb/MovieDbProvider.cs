
using Popcorn.SearchProviders.Exceptions;
using TMDbLib.Client;
using TMDbLib.Objects.General;
using TMDbLib.Objects.Movies;
using TMDbLib.Objects.Search;
using TMDbLib.Objects.TvShows;

namespace Popcorn.SearchProviders.MovieDb
{
    public class MovieDbProvider
    {
        private TMDbClient _client = new TMDbClient("b8af6dd235f0a34dd87d3697acd79b29");
        private List<Genre> _genreCache = [];

        public async Task<List<MovieDbSearchResult>> Search(string query)
        {
            var results = new List<MovieDbSearchResult>();
            try
            {
                if (!_client.HasConfig)
                {
                    await LoadInitialData();
                }
                
                var searchResult = await _client.SearchMultiAsync(query);
                
                results.AddRange(searchResult!.Results!.Where(r => r.MediaType is MediaType.Movie or MediaType.Tv or MediaType.Person).Select(r => r switch
                {
                    SearchMovie m => new MovieDbSearchResult
                    {
                        MediaType = m.MediaType,
                        Id = m.Id,
                        Title = m.Title ?? m.OriginalTitle ?? string.Empty,
                        Description = m.Overview ?? string.Empty,
                        ReleaseDate = m.ReleaseDate,
                        PosterPath = BuildPosterUrl(m.PosterPath),
                        BackdropPath = BuildOriginalUrl(m.BackdropPath),
                        Genres = GetGenresFromIds(m.GenreIds)
                    },

                    SearchTv t => new MovieDbSearchResult
                    {
                        MediaType = t.MediaType,
                        Id = t.Id,
                        Title = t.Name ?? t.OriginalName ?? string.Empty,
                        Description = t.Overview ?? string.Empty,
                        ReleaseDate = t.FirstAirDate,
                        PosterPath = BuildPosterUrl(t.PosterPath),
                        BackdropPath = BuildOriginalUrl(t.BackdropPath),
                        Genres = GetGenresFromIds(t.GenreIds)
                    },

                    SearchPerson p => new MovieDbSearchResult
                    {
                        MediaType = p.MediaType,
                        Id = p.Id,
                        Title = p.Name ?? string.Empty,
                        ProfilePath = BuildOriginalUrl(p.ProfilePath)
                    },

                    _ => null
                }).ToList()!);
            }
            catch (Exception e)
            {
                throw new MovieDbException("Error getting MovieDb search results", e);
            }
        
            return results;
        }
        
        public async Task<Movie?> GetMovieDetails(int id)
        {
            if (!_client.HasConfig)
            {
                await LoadInitialData();
            }
            
            var movie = await _client.GetMovieAsync(id);
            if (movie is null) return null;
            
            movie.PosterPath = BuildPosterUrl(movie.PosterPath);
            movie.BackdropPath = BuildOriginalUrl(movie.BackdropPath);
            return movie;
        }

        public async Task<TvShow?> GetTvDetails(int id)
        {
            if (!_client.HasConfig)
            {
                await LoadInitialData();
            }
            
            var tvShow = await _client.GetTvShowAsync(id);
            if (tvShow is null) return null;
            
            tvShow.PosterPath = BuildPosterUrl(tvShow.PosterPath);
            tvShow.BackdropPath = BuildOriginalUrl(tvShow.BackdropPath);
            return tvShow;
        }

        private string BuildPosterUrl(string? imageUrl)
        {
            return $"{_client.Config.Images!.BaseUrl}{_client.Config.Images!.PosterSizes![2]}{imageUrl}";
        }
        
        private string BuildOriginalUrl(string? imageUrl)
        {
            return $"{_client.Config.Images!.BaseUrl}original{imageUrl}";
        }

        private async Task LoadInitialData()
        {
            await _client.GetConfigAsync();
            _genreCache = await _client.GetMovieGenresAsync() ?? [];
            _genreCache.AddRange(await _client.GetTvGenresAsync() ?? []);
        }
        
        private List<string> GetGenresFromIds(List<int>? ids)
        {
            if(ids is null)
            {
                return [];
            }
            
            List<string> genres = [];
            foreach (var foundGenre in ids.Select(id => _genreCache.FirstOrDefault(g => g.Id == id)))
            {
                if (foundGenre?.Name is null)
                {
                    continue;
                }
                genres.Add(foundGenre.Name);
            }
            return genres;
        }

    }
}
