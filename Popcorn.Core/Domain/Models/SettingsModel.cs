using Popcorn.Domain.Enum;

namespace Popcorn.Domain.Models;

public class SettingsModel
{
    /// <summary>Legacy (pre player list) switch: true = Popcorn player, false = default app. Only used to seed <see cref="Players"/>.</summary>
    public bool UseInternalPlayer { get; set; }
    /// <summary>Magnet / .torrent links are downloaded by the built-in torrent client (true) or handed to the system's torrent program.</summary>
    public bool UseInternalTorrentClient { get; set; } = true;
    public List<string> PrefferedAudioLanguages { get; set; } = new();
    public List<string> PrefferedSubtitleLanguages { get; set; } = new();

    /// <summary>Native players in order of preference, used when the server opens a file (desktop app, <c>FileOpen</c>).</summary>
    public List<PlayerOption> Players { get; set; } = new();

    public DownloadSettings Downloads { get; set; } = new();

    /// <summary>When players skip intros/outros on their own; library items can override it.</summary>
    public SegmentAutoSkip SegmentAutoSkip { get; set; } = SegmentAutoSkip.Always;

    /// <summary>Skipping an outro that ends within 10 seconds of the end of the file skips those last seconds too.</summary>
    public bool SkipOutroTail { get; set; } = true;

    public const int MaxSegmentAutoSkipDelay = 99999;

    /// <summary>
    /// Seconds of the skip button's count-down before auto-skip jumps past an intro/outro (0 = as soon as it starts),
    /// 0..<see cref="MaxSegmentAutoSkipDelay"/>.
    /// </summary>
    public int SegmentAutoSkipDelay { get; set; } = 10;

    public const int MinLogRetentionDays = 1;
    public const int MaxLogRetentionDays = 3650;

    /// <summary>Days of log files kept in <c>logs/</c> (today counts as one); older ones are deleted by the server.</summary>
    public int LogRetentionDays { get; set; } = 7;

    /// <summary>
    /// Drops invalid entries and makes sure both built-in players exist exactly once. Missing built-ins are seeded
    /// from the legacy <see cref="UseInternalPlayer"/> flag on first run.
    /// </summary>
    public void NormalizePlayers()
    {
        Downloads ??= new(); // settings.json from before downloads existed, or a client sending null
        SegmentAutoSkipDelay = Math.Clamp(SegmentAutoSkipDelay, 0, MaxSegmentAutoSkipDelay);
        LogRetentionDays = LogRetentionDays <= 0 ? 7 : Math.Clamp(LogRetentionDays, MinLogRetentionDays, MaxLogRetentionDays);

        Players = (Players ?? new())
            .Where(p => p is not null)
            .Where(p => p.Type switch
            {
                PlayerOptionType.Program => !string.IsNullOrWhiteSpace(p.Path),
                PlayerOptionType.Script => !string.IsNullOrWhiteSpace(p.Command),
                _ => true,
            })
            .DistinctBy(p => p.IsBuiltIn ? p.Type.ToString() : p.Id)
            .ToList();

        var builtIns = UseInternalPlayer || Players.Count > 0
            ? new[] { PlayerOptionType.Popcorn, PlayerOptionType.Default }
            : new[] { PlayerOptionType.Default, PlayerOptionType.Popcorn };

        var insertAt = 0;
        foreach (var type in builtIns)
        {
            if (Players.Any(p => p.Type == type)) continue;
            Players.Insert(Math.Min(insertAt++, Players.Count), new PlayerOption { Id = type.ToString().ToLowerInvariant(), Type = type });
        }
    }
}
