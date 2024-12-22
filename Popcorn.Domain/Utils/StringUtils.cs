using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Popcorn.Shared.Utils
{
    public static class StringUtils
    {
        public static string FormatBytes(long bytes, bool perSecond = false)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double length = bytes;
            int order = 0;
            while (length >= 1024 && order < sizes.Length - 1)
            {
                order++;
                length /= 1024;
            }

            string sizeString = string.Format("{0:0.##} {1}", length, sizes[order]);
            if (perSecond)
            {
                sizeString += "/s";
            }

            return sizeString;
        }

        private static readonly string[] TagsToRemove = { "bluray", "blu-ray", "dts-hd", "aac", "dsr" };
        public static (string, List<string>) SanitizeFilename(string filename, List<string> tags)
        {
            if(tags == null) tags = new List<string>();

            var name = filename.Trim();
            var containsHyphen = name.Contains("-") && name.Count(c => c == '-') > 2;
            var containsUnderscore = name.Contains("_") && name.Count(c => c == '_') > 2;
            if (!name.Contains(" ") && (containsHyphen || containsUnderscore))
            {
                if(containsHyphen)
                {
                    name = name.Replace('-', ' ');
                }
                if (containsUnderscore)
                {
                    name = name.Replace('_', ' ');
                }
            }
            var removedTags = new List<string>();
            
            // Remove extension
            var extension = Path.GetExtension(filename);
            if(!string.IsNullOrWhiteSpace(extension))
            {
                name = name.Replace(extension, "");
            }

            // If contains no spaces, but more than 1 dot - replace with spaces
            if (!name.Contains(" ") && name.Count(c => c == '.') > 1)
            {
                name = name.Replace(".", " ");
            }

            // If contains any of tags - remove
            foreach (var tag in tags)
            {
                if (name.ToLower().Contains(tag))
                {
                    var index = name.ToLower().IndexOf(tag);
                    name = name.Remove(index, tag.Length);
                    removedTags.Add(tag);
                }
            }

            // If contains [ ], ( ) and some contents - remove with contents
            var regex = new Regex(@"\[(.*?)\]|\((.*?)\)");
            var matches = regex.Matches(name).Cast<Match>().Select(m => m.Value);
            name = regex.Replace(name, "");

            // If contains two+ spaces - remove them
            while (name.Contains("  "))
            {
                name = name.Replace("  ", " ");
            }
            
            if(removedTags.Count == 0)
            {
                removedTags = tags;
            }
            if(string.IsNullOrWhiteSpace(name))
            {
                return (filename, removedTags);
            }

            return (name, removedTags);
        }
    }
}
