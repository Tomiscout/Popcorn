using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Popcorn.SearchProviders.Providers.Rarbg.DTO
{
    public class RarbgSearchError
    {
        [JsonPropertyName("error")]
        public string Message { get; set; }

        [JsonPropertyName("error_code")]
        public int ErrorCode { get; set; }
    }
}
