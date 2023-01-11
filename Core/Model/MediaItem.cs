using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoBlazor.Core.Model
{
    public class MediaItem
    {
        public Guid Id { get; set; }
        public string Title { get; set; }
        public string Image { get; set; }
        public string ImagePath { get; set; }
        public string FolderDirectory { get; set; }
        public IList<string> Files { get;set; }
        public IList<PlaybackHistory> History { get;set; }
    }
}
