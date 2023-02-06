using DM.MovieApi.ApiResponse;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public class MovieDbApiError
    {
        public Exception Exception { get; set; }

        [DataMember(Name = "status_code")]
        public int StatusCode { get;  set; }

        [DataMember(Name = "status_message")]
        public string Message { get; set; }

        public override string ToString()
        {
            DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(10, 2);
            defaultInterpolatedStringHandler.AppendLiteral("Status: ");
            defaultInterpolatedStringHandler.AppendFormatted(StatusCode);
            defaultInterpolatedStringHandler.AppendLiteral(": ");
            defaultInterpolatedStringHandler.AppendFormatted(Message);
            return defaultInterpolatedStringHandler.ToStringAndClear();
        }
    }
}
