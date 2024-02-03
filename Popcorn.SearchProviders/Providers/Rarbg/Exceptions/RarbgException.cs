using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Popcorn.SearchProviders.Providers.Rarbg.Exceptions
{
    public class RarbgException : Exception
    {
        public RarbgException(string message) : base(message)
        {
        }

        public RarbgException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
