using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Exceptions
{
    public class MovieDbException : Exception
    {
        public MovieDbException(string message) : base(message)
        {
        }

        public MovieDbException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
