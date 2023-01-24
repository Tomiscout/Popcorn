
namespace VideoBlazor.SearchProviders.Exceptions
{
    public class SearchFailedException : Exception
    {
        public SearchFailedException(string message) : base(message)
        {
        }
        
        public SearchFailedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
