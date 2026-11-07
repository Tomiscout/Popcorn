using System.Text.Json.Serialization;

namespace Popcorn.Server.Models.Requests;

/// <summary><c>UpdateStatus</c>: whether this install can update itself (the installer is next to the server).</summary>
public class UpdateStatus
{
    public bool Supported { get; init; }
    public required string CurrentVersion { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<UpdateCheckStatus>))]
public enum UpdateCheckStatus
{
    Unsupported,
    UpToDate,
    Available,
    Failed,
}

/// <summary><c>UpdateCheck</c> answer.</summary>
public class UpdateCheckResult
{
    public UpdateCheckStatus Status { get; init; }
    /// <summary>The newer release (<see cref="UpdateCheckStatus.Available"/>).</summary>
    public string? Version { get; init; }
    /// <summary>Its release notes, markdown as written on GitHub.</summary>
    public string? Notes { get; init; }
    public string? Error { get; init; }
}
