using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.SearchProviders.MovieDb
{
    public class MovieDbSearchResult
    {
        public MovieDbMediaType MediaType { get; set; }
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string PosterPath { get; set; }
        public string BackdropPath { get; set; }
        public string ProfilePath { get; set; }
        public List<string> Genres { get; set; }
    }
}
