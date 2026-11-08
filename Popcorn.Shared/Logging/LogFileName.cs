using System.Globalization;
using System.Text.RegularExpressions;

namespace Popcorn.Shared.Logging;

/// <summary>
/// Names of the daily log files in <c>&lt;data&gt;/logs</c>: <c>YYYY-MM-DD-&lt;App&gt;.txt</c> (date first, so the folder
/// sorts by day), <c>YYYY-MM-DD-&lt;App&gt;_001.txt</c> when another process of the same app holds the day's file. The Rust
/// apps (Popcorn.Linux, Popcorn.Installer) write the same names. Also linked into Popcorn.Windows, keep it dependency-free.
/// </summary>
public static partial class LogFileName
{
    public static string Format(string app, DateOnly day, int index = 0) =>
        $"{day:yyyy-MM-dd}-{app}{(index > 0 ? $"_{index:000}" : "")}.txt";

    public static bool TryParse(string fileName, out string app, out DateOnly day)
    {
        var match = CurrentName().Match(fileName);
        if (match.Success && DateOnly.TryParseExact(match.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out day))
        {
            app = match.Groups["app"].Value;
            return true;
        }

        app = "";
        day = default;
        return false;
    }

    /// <summary>Old Serilog rolling name, <c>&lt;App&gt;-YYYYMMDD.txt</c> (<c>_001</c> suffix when shared).</summary>
    public static bool TryParseLegacy(string fileName, out string app, out DateOnly day, out int index)
    {
        var match = LegacyName().Match(fileName);
        if (match.Success && DateOnly.TryParseExact(match.Groups["day"].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out day))
        {
            app = match.Groups["app"].Value;
            index = match.Groups["index"].Success ? int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture) : 0;
            return true;
        }

        app = "";
        day = default;
        index = 0;
        return false;
    }

    /// <summary>
    /// Renames old <c>&lt;App&gt;-YYYYMMDD.txt</c> files to the date-first names. Files still open by a running app are
    /// left alone where the OS refuses (Windows); on Linux the writer just goes on in the renamed file.
    /// </summary>
    public static int MigrateLegacyNames(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        var renamed = 0;
        foreach (var path in Directory.EnumerateFiles(folder, "*.txt"))
        {
            if (!TryParseLegacy(Path.GetFileName(path), out var app, out var day, out var index)) continue;
            for (var i = index; i < 1000; i++)
            {
                var target = Path.Combine(folder, Format(app, day, i));
                if (File.Exists(target)) continue;
                try
                {
                    File.Move(path, target);
                    renamed++;
                }
                catch (IOException)
                {
                    // in use
                }
                catch (UnauthorizedAccessException)
                {
                }

                break;
            }
        }

        return renamed;
    }

    [GeneratedRegex(@"^(?<day>\d{4}-\d{2}-\d{2})-(?<app>.+?)(?:_(?<index>\d{3}))?\.txt$")]
    private static partial Regex CurrentName();

    [GeneratedRegex(@"^(?<app>.+)-(?<day>\d{8})(?:_(?<index>\d{3}))?\.txt$")]
    private static partial Regex LegacyName();
}
