using System.Text.Json.Serialization;

namespace Popcorn.Domain.Models;

[JsonConverter(typeof(JsonStringEnumConverter<PlayerOptionType>))]
public enum PlayerOptionType
{
    /// <summary>Native Popcorn player (Popcorn.Player.Linux / Popcorn.Player.Windows).</summary>
    Popcorn,
    /// <summary>Program the OS associates with the file type (xdg-mime / Windows file associations).</summary>
    Default,
    /// <summary>User-picked executable, launched with the file path as its only argument.</summary>
    Program,
    /// <summary>User-written shell command line; <see cref="PlayerOption.ArgsPlaceholder"/> is replaced with the quoted file path.</summary>
    Script,
}

/// <summary>
/// One entry of the ordered native player list. When a file is opened the first enabled entry that launches
/// successfully is used, the rest are fallbacks.
/// </summary>
public class PlayerOption
{
    public const string ArgsPlaceholder = "%args";

    public string Id { get; set; } = Guid.NewGuid().ToString();
    public PlayerOptionType Type { get; set; }
    public string? Name { get; set; }
    /// <summary>Executable path for <see cref="PlayerOptionType.Program"/>.</summary>
    public string? Path { get; set; }
    /// <summary>Command line for <see cref="PlayerOptionType.Script"/>.</summary>
    public string? Command { get; set; }
    public bool Enabled { get; set; } = true;

    [JsonIgnore]
    public bool IsBuiltIn => Type is PlayerOptionType.Popcorn or PlayerOptionType.Default;
}
