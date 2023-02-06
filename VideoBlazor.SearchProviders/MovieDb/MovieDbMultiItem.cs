using DM.MovieApi.MovieDb.Companies;
using DM.MovieApi.MovieDb.Genres;
using DM.MovieApi.MovieDb.People;
using DM.MovieApi.MovieDb.TV;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public class MovieDbMultiItem
    {
        [JsonPropertyName("media_type")]
        public string MediaType { get; set; }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("adult")]
        public bool IsAdultThemed { get; set; }

        [JsonPropertyName("backdrop_path")]
        public string BackdropPath { get; set; }

        [JsonPropertyName("genre_ids")]
        public IReadOnlyList<int> GenreIds { get; set; }

        public IReadOnlyList<Genre> Genres { get; set; }

        [JsonPropertyName("original_title")]
        public string OriginalTitle { get; set; }

        [JsonPropertyName("overview")]
        public string Overview { get; set; }

        [JsonPropertyName("release_date")]
        public DateTime? ReleaseDate { get; set; }

        [JsonPropertyName("poster_path")]
        public string PosterPath { get; set; }

        [JsonPropertyName("popularity")]
        public double Popularity { get; set; }

        [JsonPropertyName("video")]
        public bool Video { get; set; }

        [JsonPropertyName("vote_average")]
        public double VoteAverage { get; set; }

        [JsonPropertyName("vote_count")]
        public int VoteCount { get; set; }

        //TV
        [JsonPropertyName("created_by")]
        public IReadOnlyList<TVShowCreator> CreatedBy { get; set; }

        [JsonPropertyName("episode_run_time")]
        public IReadOnlyList<int> EpisodeRunTime { get; set; }

        [JsonPropertyName("first_air_date")]
        public DateTime? FirstAirDate { get; set; }

        [JsonPropertyName("homepage")]
        public string Homepage { get; set; }

        [JsonPropertyName("in_production")]
        public bool InProduction { get; set; }

        [JsonPropertyName("languages")]
        public IReadOnlyList<string> Languages { get; set; }

        [JsonPropertyName("last_air_date")]
        public DateTime LastAirDate { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

     /*   [JsonPropertyName("networks")]
        public IReadOnlyList<Network> Networks { get; set; }*/

        [JsonPropertyName("number_of_episodes")]
        public int NumberOfEpisodes { get; set; }

        [JsonPropertyName("number_of_seasons")]
        public int NumberOfSeasons { get; set; }

        [JsonPropertyName("origin_country")]
        public IReadOnlyList<string> OriginCountry { get; set; }

        [JsonPropertyName("original_language")]
        public string OriginalLanguage { get; set; }

        [JsonPropertyName("original_name")]
        public string OriginalName { get; set; }

        [JsonPropertyName("production_companies")]
        public IReadOnlyList<ProductionCompanyInfo> ProductionCompanies { get; set; }

        [JsonPropertyName("seasons")]
        public IReadOnlyList<Season> Seasons { get; set; }

        //Person
        [JsonPropertyName("also_known_as")]
        public IReadOnlyList<string> AlsoKnownAs { get; set; }

        [JsonPropertyName("biography")]
        public string Biography { get; set; }

        [JsonPropertyName("birthday")]
        public DateTime Birthday { get; set; }

        [JsonPropertyName("deathday")]
        public DateTime? Deathday { get; set; }

        [JsonPropertyName("gender")]
        public Gender Gender { get; set; }

        [JsonPropertyName("imdb_id")]
        public string ImdbId { get; set; }

        [JsonPropertyName("place_of_birth")]
        public string PlaceOfBirth { get; set; }

        [JsonPropertyName("profile_path")]
        public string ProfilePath { get; set; }
    }
}
