
namespace Popcorn.Domain.Interfaces;

public interface INativeProcessRunner {
    public string? ExecuteCommandRead(string command, string args = "");
    public void ExecuteCommandDetached(string command, string args = "");
}