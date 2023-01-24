using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Providers.Rarbg.DTO
{
    public class RarbgResponseWrapper
    {
        [JsonPropertyName("torrent_results")]
        public List<RarbgSearchResult> Results { get; set; }
    }
}
