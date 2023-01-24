
using VideoBlazor.SearchProviders.Enum;

namespace VideoBlazor.SearchProviders.Model
{
    public class SearchProvider
    {
        public string Title { get; set; }
        public string Website { get; set; }
        public string? Icon { get; set; }
        public SearchSourceType Type { get;set; }
    }
}
