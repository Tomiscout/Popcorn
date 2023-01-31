using VideoBlazor.Core.Enums;

namespace VideoBlazor.Core.Model
{
    public class SearchQueryStatus
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public ProcessStatus Status { get; set; }
    }
}
