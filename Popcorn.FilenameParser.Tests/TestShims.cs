using System.IO;
using System.Text.RegularExpressions;

namespace Popcorn.FilenameParser.Tests
{
    // Resolution buckets the Radarr quality fixtures are written against (the parser reports plain ints).
    public enum Resolution
    {
        Unknown = 0,
        R360p = 360,
        R480p = 480,
        R540p = 540,
        R576p = 576,
        R720p = 720,
        R1080p = 1080,
        R2160p = 2160,
    }

    public static class TestStringExtensions
    {
        private static readonly Regex NormalizeRegex = new(@"((?:\b|_)(?<!^)([aà](?!$)|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", RegexOptions.IgnoreCase);

        // Upstream compares parsed titles through its title normalisation (lower case, no punctuation or articles).
        public static string CleanTitle(this string title) => NormalizeRegex.Replace(title, string.Empty).ToLowerInvariant();

        // NzbDrone.Test.Common: turn a Windows path into the current OS's form.
        public static string AsOsAgnostic(this string path)
        {
            if (!System.OperatingSystem.IsWindows())
            {
                if (path.Length > 2 && path[1] == ':')
                {
                    path = path.Replace(":", "");
                    path = Path.DirectorySeparatorChar + path;
                }

                path = path.Replace("\\", Path.DirectorySeparatorChar.ToString());
            }

            return path;
        }

        // NzbDrone.Common.Expansive (subset): replaces {name} with the matching property of the object.
        public static string Expand(this string source, object values)
        {
            foreach (var property in values.GetType().GetProperties())
            {
                source = source.Replace("{" + property.Name + "}", property.GetValue(values)?.ToString());
            }

            return source;
        }
    }

    // Sonarr's NzbDrone.Core.Indexers.XmlCleaner (only what the Unicode fixture uses).
    public static class XmlCleaner
    {
        private static readonly Regex ReplaceUnicodeRegex = new Regex(@"[^\x09\x0A\x0D\x20-\uD7FF\uE000-\uFFFD]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ReplaceUnicode(string content)
        {
            return ReplaceUnicodeRegex.Replace(content, string.Empty);
        }
    }
}
