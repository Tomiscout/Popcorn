using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;

namespace Popcorn.SearchProviders.Omdb
{
    public class OmdbProvider
    {
        private const string ApiUrl = "https://www.omdbapi.com/";
        private const string ApiKey = "3e4598f7";

        public async Task<OmdbResult> GetRatings(string imdb)
        {
            using HttpClient client = new HttpClient();
            var parameters = new Dictionary<string, string>
            {
                { "i", imdb },
                { "apiKey", ApiKey },
            };
            
            try
            {
                var content = await client.GetStringAsync(new Uri(QueryHelpers.AddQueryString(ApiUrl, parameters)));
                var ratings = JsonSerializer.Deserialize<OmdbApiResult>(content);
                if (ratings is null)
                {
                    throw new OmdbException("Failed to parse Rargb login response");
                }

                
                
                return new OmdbResult()
                {
                    ImdbRating = ratings.ImdbRating == "N/A" ? null : ratings.ImdbRating,
                    Metascore = ratings.Metascore == "N/A" ? null : int.Parse(ratings.Metascore),
                    RTRating = ParseRTScore(ratings),
                    Awards = ratings.Awards == "N/A" ? null : ratings.Awards,
                };
            }
            catch (OmdbException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new OmdbException("Failed to get Omdb response", e);
            }
        }
        
        private static int? ParseRTScore(OmdbApiResult ratings)
        {
            var rtScoreString = ratings.Ratings.FirstOrDefault(r => r.Source == "Rotten Tomatoes")?.Value;
            rtScoreString = rtScoreString == "N/A" ? null : rtScoreString;
            int? rtScore = null;
            if (rtScoreString is not null)
            {
                int parsedScore;
                if (int.TryParse(rtScoreString.Substring(0, rtScoreString.IndexOf("%")), out parsedScore))
                {
                    rtScore = parsedScore;
                }
            }
            return rtScore;
        }
    }
}
