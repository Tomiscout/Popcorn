using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.Utils
{
    public static class PageUtils
    {

        public static string GetImdbIdFromUrl(string url)
        {
            var imdbId = url.Split('/')[4];
            if (imdbId.Contains("?"))
            {
                imdbId = imdbId.Split('?').First();
            }
            return imdbId;
        }
    }
}
