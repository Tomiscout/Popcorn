using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public static class MovieDbRequests
    {
        private const string _apiKey = "b8af6dd235f0a34dd87d3697acd79b29";
        private const string _baseUrl = "http://api.themoviedb.org/3/";

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
                result.Json = json;
                return result;
            }
            catch (Exception e)
            {
                var error = new MovieDbApiResponse
                {
                    // This will throw up if the error is page number = 0; the resultant json will be: {"errors":["page must be greater than 0"]}
                    // in other words, the json will not include a status_code. Asked the api devs and this is a known issue they are working on.
                    // What to do? Nothing really, the page guard at the top of the method will keep the page number > 0.
                    Error = new MovieDbApiError() { Message = e.Message, Exception = e },
                    CommandText = response.RequestMessage?.RequestUri?.ToString() ?? "response message/uri was null",
                    Json = json,
                };

                return error;
            }
        }

        public static async Task<string> GetTvImdbId(int id)
        {
            var param = new Dictionary<string, string>();

            string command = $"{_baseUrl}tv/{id}/external_ids";
            using HttpClient client = CreateClient();
            var cmd = CreateCommand(command, param);

            var json = await client.GetStringAsync(cmd);
            if(json is null) return null;

            var result = JsonSerializer.Deserialize<MovieDbTVIdRequest>(json);
            return result.ImdbId;
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
            client.BaseAddress = new Uri(_baseUrl);

            return client;
        }

        private static string CreateCommand(string rootCommand, IDictionary<string, string> parameters)
        {
            if(!parameters.ContainsKey("api_key"))
            {
                parameters.Add("api_key", _apiKey);
            }
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
