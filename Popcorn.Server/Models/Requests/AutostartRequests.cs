namespace Popcorn.Server.Models.Requests;

/// <summary>Answer of <c>Autostart</c> / <c>AutostartSet</c>: whether the server starts at sign-in.</summary>
public class AutostartState
{
    /// <summary>False = not managed on this platform (yet).</summary>
    public bool Supported { get; init; }
    public bool Enabled { get; init; }
}

public class AutostartSetRequest
{
    public bool Enabled { get; init; }
}
