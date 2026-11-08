using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Popcorn.Shared.Logging;
using Popcorn.Shared.Utils;

namespace Popcorn.Server.Logs;

/// <summary>Filters of Settings → Logs. Empty lists = everything; apps and levels are exclusions, so apps whose first
/// file only appears later (the player's, a new day's) show up without touching the filter.</summary>
public sealed class LogQuery
{
    /// <summary>First day, inclusive (local time).</summary>
    public DateOnly? From { get; set; }

    /// <summary>Last day, inclusive. Set = a closed range, the viewer stops polling.</summary>
    public DateOnly? To { get; set; }

    public List<string>? ExcludeApps { get; set; }
    public List<string>? ExcludeLevels { get; set; }
    public string? Search { get; set; }

    /// <summary>Newest entries returned by a full query (no cursor).</summary>
    public int Limit { get; set; } = 5000;

    /// <summary>Bytes already read per file name, from the previous answer: only what was appended since is read and
    /// returned. Files missing from it (created since) are read from the start.</summary>
    public Dictionary<string, long>? Cursor { get; set; }
}

public sealed record LogEntry(string Time, string App, string Level, string Message, string? Detail, string? Source);

public sealed record LogApp(string Name, string Label);

public sealed class LogQueryResult
{
    public required List<LogEntry> Entries { get; init; }
    public required Dictionary<string, long> Cursor { get; init; }

    /// <summary>Every app with a log file, whatever the filters.</summary>
    public required List<LogApp> Apps { get; init; }

    /// <summary>Day of the oldest log entry on disk (date picker lower bound).</summary>
    public DateOnly? OldestDay { get; init; }

    /// <summary>Matches per level (app/search filters applied, level filter not) and per app (the other way round), of
    /// the entries this answer covers: the whole range for a full query, the new ones for a cursor query.</summary>
    public required Dictionary<string, int> LevelCounts { get; init; }

    public required Dictionary<string, int> AppCounts { get; init; }

    /// <summary>More entries matched than <see cref="LogQuery.Limit"/>; the oldest were left out.</summary>
    public int Skipped { get; init; }
}

/// <summary>
/// Reads every app's log files in <c>&lt;data&gt;/logs</c> into one time-ordered list: the daily
/// <c>YYYY-MM-DD-&lt;App&gt;.txt</c> files (Serilog and the Rust apps: <c>2026-11-08 01:32:45.708 [+02:00] [INF] message</c>,
/// lines without a time stamp continue the entry above, e.g. stack traces) and CEF's <c>cef.log</c> /
/// <c>cef-player.log</c> (<c>[pid:tid:MMDD/HHMMSS.micro:LEVEL:file.cc:42] message</c>, recreated at each start).
/// </summary>
public static partial class LogReader
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";

    private sealed record LogFile(string Path, string Name, string App, DateOnly? Day, long Length, DateTime LastWrite)
    {
        public bool IsCef => Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
    }

    public static string Folder => DirectoryUtils.LogsFolder;

    public static LogQueryResult Query(LogQuery query)
    {
        var files = ListFiles();
        var excludedApps = new HashSet<string>(query.ExcludeApps ?? [], StringComparer.OrdinalIgnoreCase);
        var excludedLevels = new HashSet<string>(query.ExcludeLevels ?? [], StringComparer.OrdinalIgnoreCase);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        var fromKey = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toKey = query.To?.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var levelCounts = new Dictionary<string, int>();
        var appCounts = new Dictionary<string, int>();
        var cursor = new Dictionary<string, long>();
        var matches = new List<(LogEntry Entry, int File, int Line)>();

        for (var f = 0; f < files.Count; f++)
        {
            var file = files[f];
            if (file.Day is { } day && (day < query.From || day > query.To)) continue;
            if (file.IsCef && fromKey is not null && DateOnly.FromDateTime(file.LastWrite) < query.From) continue;

            long start = 0;
            if (query.Cursor is not null && query.Cursor.TryGetValue(file.Name, out var read))
                start = read <= file.Length ? read : 0; // shorter: recreated (CEF) → from the top
            var (lines, end) = ReadLines(file.Path, start);
            cursor[file.Name] = end;
            if (lines.Count == 0) continue;

            var entries = file.IsCef ? ParseCef(lines, file) : ParseDaily(lines, file);
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (fromKey is not null && string.CompareOrdinal(entry.Time, fromKey) < 0) continue;
                if (toKey is not null && string.CompareOrdinal(entry.Time, toKey) >= 0) continue;
                if (search is not null && !Contains(entry, search)) continue;

                var appShown = !excludedApps.Contains(entry.App);
                var levelShown = !excludedLevels.Contains(entry.Level);
                if (appShown) levelCounts[entry.Level] = levelCounts.GetValueOrDefault(entry.Level) + 1;
                if (levelShown) appCounts[entry.App] = appCounts.GetValueOrDefault(entry.App) + 1;
                if (appShown && levelShown) matches.Add((entry, f, i));
            }
        }

        matches.Sort((a, b) =>
        {
            var byTime = string.CompareOrdinal(a.Entry.Time, b.Entry.Time);
            if (byTime != 0) return byTime;
            return a.File != b.File ? a.File.CompareTo(b.File) : a.Line.CompareTo(b.Line);
        });

        var skipped = 0;
        if (query.Cursor is null && matches.Count > query.Limit)
        {
            skipped = matches.Count - query.Limit;
            matches.RemoveRange(0, skipped);
        }

        return new LogQueryResult
        {
            Entries = matches.ConvertAll(m => m.Entry),
            Cursor = cursor,
            Apps = files.Select(f => f.App).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
                .Select(app => new LogApp(app, Label(app))).ToList(),
            OldestDay = OldestDay(files),
            LevelCounts = levelCounts,
            AppCounts = appCounts,
            Skipped = skipped,
        };
    }

    private static List<LogFile> ListFiles()
    {
        var files = new List<LogFile>();
        if (!Directory.Exists(Folder)) return files;
        foreach (var info in new DirectoryInfo(Folder).EnumerateFiles())
        {
            if (info.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase))
            {
                // cef.log → CEF, cef-player.log → CEF.Player
                var app = Path.GetFileNameWithoutExtension(info.Name);
                app = app.Equals("cef", StringComparison.OrdinalIgnoreCase)
                    ? "CEF"
                    : "CEF." + string.Concat(app.Replace("cef-", "", StringComparison.OrdinalIgnoreCase)
                        .Split('-').Select(Capitalize));
                files.Add(new LogFile(info.FullName, info.Name, app, null, info.Length, info.LastWriteTime));
            }
            else if (LogFileName.TryParse(info.Name, out var app, out var day)
                     || LogFileName.TryParseLegacy(info.Name, out app, out day, out _))
            {
                files.Add(new LogFile(info.FullName, info.Name, app, day, info.Length, info.LastWriteTime));
            }
        }

        // Stable order for entries with the same time stamp.
        files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return files;
    }

    private static DateOnly? OldestDay(List<LogFile> files)
    {
        DateOnly? oldest = null;
        foreach (var file in files)
        {
            var day = file.Day;
            if (file.IsCef && file.Length > 0)
            {
                var (lines, _) = ReadLines(file.Path, 0, maxBytes: 4096);
                var first = ParseCef(lines, file).FirstOrDefault();
                if (first is not null && DateOnly.TryParseExact(first.Time[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var cefDay))
                    day = cefDay;
            }

            if (day is not null && (oldest is null || day < oldest)) oldest = day;
        }

        return oldest;
    }

    /// <summary>Complete lines from <paramref name="start"/>; the end offset stops after the last line break, so a line
    /// still being written is read whole by the next query.</summary>
    private static (List<string> Lines, long End) ReadLines(string path, long start, int maxBytes = int.MaxValue)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (start >= stream.Length) return ([], Math.Min(start, stream.Length));
            stream.Position = start;
            var length = (int)Math.Min(stream.Length - start, maxBytes);
            var buffer = new byte[length];
            stream.ReadExactly(buffer, 0, length);
            var last = Array.LastIndexOf(buffer, (byte)'\n');
            if (last < 0) return ([], start);
            var text = Encoding.UTF8.GetString(buffer, 0, last + 1);
            var lines = text.Split('\n');
            var result = new List<string>(lines.Length);
            foreach (var line in lines.AsSpan(0, lines.Length - 1)) result.Add(line.TrimEnd('\r'));
            return (result, start + last + 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ([], start);
        }
    }

    private static List<LogEntry> ParseDaily(List<string> lines, LogFile file)
    {
        var entries = new List<LogEntry>();
        StringBuilder? detail = null;
        LogEntry? current = null;

        void Flush()
        {
            if (current is null) return;
            entries.Add(detail is { Length: > 0 } ? current with { Detail = detail.ToString().TrimEnd() } : current);
            current = null;
            detail = null;
        }

        foreach (var line in lines)
        {
            var match = DailyLine().Match(line);
            if (match.Success)
            {
                Flush();
                var time = match.Groups["time"].Value;
                time = time.Length >= TimeFormat.Length ? time[..TimeFormat.Length] : time + ".000"[(time.Length - 19)..];
                current = new LogEntry(time, file.App, match.Groups["level"].Value.ToUpperInvariant(),
                    match.Groups["msg"].Value, null, null);
            }
            else if (current is not null)
            {
                (detail ??= new StringBuilder()).Append(line).Append('\n');
            }
            else if (line.Length > 0)
            {
                // continuation of an entry before the cursor / file start without a time stamp
                var time = (file.Day ?? DateOnly.FromDateTime(file.LastWrite)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                           + " 00:00:00.000";
                current = new LogEntry(time, file.App, "INF", line, null, null);
            }
        }

        Flush();
        return entries;
    }

    private static List<LogEntry> ParseCef(List<string> lines, LogFile file)
    {
        var entries = new List<LogEntry>();
        foreach (var line in lines)
        {
            var match = CefLine().Match(line);
            if (!match.Success)
            {
                if (entries.Count > 0 && line.Length > 0)
                {
                    var last = entries[^1];
                    entries[^1] = last with { Detail = last.Detail is null ? line : last.Detail + "\n" + line };
                }

                continue;
            }

            var month = int.Parse(match.Groups["mo"].Value, CultureInfo.InvariantCulture);
            // No year in CEF's lines: the file's, one less for December lines in a file last written in January.
            var year = month > file.LastWrite.Month ? file.LastWrite.Year - 1 : file.LastWrite.Year;
            var time = $"{year:0000}-{match.Groups["mo"].Value}-{match.Groups["d"].Value} {match.Groups["h"].Value}:" +
                       $"{match.Groups["mi"].Value}:{match.Groups["s"].Value}.{match.Groups["ms"].Value}";
            var level = match.Groups["level"].Value switch
            {
                "INFO" => "INF",
                "WARNING" => "WRN",
                "ERROR" => "ERR",
                "FATAL" => "FTL",
                _ => "VRB", // VERBOSE1...
            };
            entries.Add(new LogEntry(time, file.App, level, match.Groups["msg"].Value, null, match.Groups["src"].Value));
        }

        return entries;
    }

    private static bool Contains(LogEntry entry, string search) =>
        entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
        || (entry.Detail?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
        || (entry.Source?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>Popcorn.Server → Server, CEF.Player → CEF Player.</summary>
    private static string Label(string app)
    {
        var label = app.StartsWith("Popcorn.", StringComparison.OrdinalIgnoreCase) ? app["Popcorn.".Length..] : app;
        label = label switch
        {
            "Linux" => "Desktop",
            "Windows" => "Desktop",
            _ => label,
        };
        return label.Replace('.', ' ');
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"^(?<time>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)(?: [+-]\d{2}:?\d{2})? \[(?<level>[A-Za-z]{3})\] ?(?<msg>.*)$")]
    private static partial Regex DailyLine();

    [GeneratedRegex(@"^\[\d+:\d+:(?<mo>\d{2})(?<d>\d{2})/(?<h>\d{2})(?<mi>\d{2})(?<s>\d{2})\.(?<ms>\d{3})\d*:(?<level>[A-Z]+\d*):(?<src>[^\]]*)\] ?(?<msg>.*)$")]
    private static partial Regex CefLine();
}
