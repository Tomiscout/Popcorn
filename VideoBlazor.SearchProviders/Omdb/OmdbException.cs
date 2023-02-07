using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Omdb
{
    public class OmdbException : Exception
    {
        public OmdbException(string message) : base(message)
        {
        }

        public OmdbException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
