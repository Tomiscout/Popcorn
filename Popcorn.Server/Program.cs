using Serilog;

namespace Popcorn.Server;

public abstract class Program
{
    public static async Task Main(string[] args)
    {
        // Example of how to pass additional host configuration if needed for standalone
        // Action<IHostBuilder> configureHost = builder =>
        // {
        //     builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
        // };

        // await using var serverHost = new PopcornServerHost(args, configureHost);
        await using var serverHost = new PopcornServerHost(args);

        try
        {
            await serverHost.StartAsync();
            Console.WriteLine($"Popcorn.Server (standalone) running. Press Ctrl+C to shut down.");
            Console.WriteLine($"Listening on: {serverHost.ListeningUrl}");

            // Keep alive for standalone execution
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true; // Prevent default termination
                cts.Cancel();
                Console.WriteLine("Shutting down...");
            };
            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { }); // Wait for cancellation
        }
        catch (OperationCanceledException)
        {
            // Expected on Ctrl+C
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            // Log detailed exception with Serilog if configured in PopcornServerHost
        }
        finally
        {
            Console.WriteLine("Attempting graceful shutdown of server host...");
            // StopAsync will be called by DisposeAsync implicitly with `await using`
        }
        Console.WriteLine("Standalone server host finished.");
    }
}