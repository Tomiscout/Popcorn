using Popcorn.Shared.Utils;
using Serilog;

namespace Popcorn.Server;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(DirectoryUtils.DataFolder, $"logs{Path.DirectorySeparatorChar}Popcorn.Server-.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        try
        {
            await using var serverHost = new PopcornServerHost(args);
            await serverHost.RunAsync(Log.Logger);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Log.Fatal(ex, "Popcorn.Server terminated unexpectedly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
