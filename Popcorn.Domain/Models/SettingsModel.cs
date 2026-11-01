namespace Popcorn.Domain.Models;

public class SettingsModel
{
    /// <summary>Legacy (pre player list) switch: true = Popcorn player, false = default app. Only used to seed <see cref="Players"/>.</summary>
    public bool UseInternalPlayer { get; set; }
    public bool UseInternalTorrentClient { get; set; }
    public List<string> PrefferedAudioLanguages { get; set; } = new();
    public List<string> PrefferedSubtitleLanguages { get; set; } = new();

    /// <summary>Native players in order of preference, used when the server opens a file (desktop app, <c>FileOpen</c>).</summary>
    public List<PlayerOption> Players { get; set; } = new();

    /// <summary>
    /// Drops invalid entries and makes sure both built-in players exist exactly once. Missing built-ins are seeded
    /// from the legacy <see cref="UseInternalPlayer"/> flag on first run.
    /// </summary>
    public void NormalizePlayers()
    {
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
