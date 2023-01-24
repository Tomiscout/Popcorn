using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Providers.Rarbg.DTO
{
    public class RarbgLoginResult
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }
    }
}
