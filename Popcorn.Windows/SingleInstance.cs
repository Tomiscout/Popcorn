using System.IO.Pipes;
using System.Runtime.InteropServices;
using Serilog;

namespace Popcorn.Windows;

/// <summary>
/// One app window per user. A second launch (start menu, tray "Open app", the player's "Find file" with
/// <c>--open-path</c>) passes its page to the running instance over a named pipe and exits; the running one comes to
/// the front and shows the page.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Name = $"Popcorn.Windows-{Environment.UserName}";

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <returns>The lock when this is the first instance; null when another one took <paramref name="page"/> over.</returns>
    public static SingleInstance? Acquire(string? page)
    {
        var mutex = new Mutex(false, $@"Local\{Name}");
        bool first;
        try
        {
            first = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            first = true;
        }

        if (first)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        HandOver(page);
        return null;
    }

    private static void HandOver(string? page)
    {
        try
        {
            // Lets the running instance bring its window to the front (otherwise Windows only flashes it).
            AllowSetForegroundWindow(-1);
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(3000);
            using var writer = new StreamWriter(pipe);
            writer.WriteLine(page ?? string.Empty);
        }
        catch (Exception e)
        {
            Log.Warning(e, "Could not hand over to the running instance");
        }
    }

    /// <summary>Calls <paramref name="onRelaunch"/> (on a pool thread) with the page of every later launch, "" for none.</summary>
    public void Listen(Action<string> onRelaunch)
    {
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(pipe);
                    var page = await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
                    onRelaunch(page.Trim());
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Log.Warning(e, "Relaunch pipe failed");
                    await Task.Delay(500);
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _mutex.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
