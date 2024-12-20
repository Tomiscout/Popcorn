namespace Popcorn.Shared.Interfaces;

public interface INativeProcessRunner
{
    public string? ExecuteCommandRead(string command, string args = "");
    public void ExecuteCommandDetached(string command, string args = "");
}