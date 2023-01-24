using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Providers.Rarbg.Exceptions
{
    public class RarbgTokenException : RarbgException
    {
        public RarbgTokenException(string message = "Rarbg token expired.") : base(message)
        {
        }
    }
}
