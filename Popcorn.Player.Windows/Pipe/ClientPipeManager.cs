using PipeMethodCalls;
using PipeMethodCalls.NetJson;
using System;
using System.IO;
using System.Threading.Tasks;
using PopcornPlayer.Shared.PipeEvents.Server;
using Popcorn.Shared.Utils;
using Popcorn.Player.Core.Core;
using PopcornPlayer.Shared;
using Microsoft.Extensions.Logging;

namespace Popcorn.Player.Windows.Pipe;

public class ClientPipeManager
{
    private readonly string _pipeFileName = DirUtils.DataFolder + "pipeName.txt";

    private PipeClientWithCallback<PopcornPlayer.Shared.IPipeServer, IPipeClient> _pipeClient;
    private readonly ILogger<ClientPipeManager> _logger;

    private readonly PlayerCore _playerCore;
    public ClientPipeManager(PlayerCore playerCore, ILogger<ClientPipeManager> logger)
    {
        _playerCore = playerCore;
        _logger = logger;
    }

    public async Task Initialize() //TODO should use some kind of bus here instead
    {
        try
        {
            var pipeName = await GetPipeName();
            if(pipeName == null)
            {
                return;
            }

            // Initialize the pipe client. Note that the type parameters are exactly
            // the same passed in the server example, but in reverse order.
            // The first type parameters indicates what the other process can do,
            // while the second one indicates the functionality this process exposes.
            _pipeClient = new PipeClientWithCallback<PopcornPlayer.Shared.IPipeServer, IPipeClient>(
                new NetJsonPipeSerializer(), pipeName, () => new PipeClient(_playerCore));

            await ConnectWithTimeout();

            await PingServerOnInit();

            // Run in the background
            _ = Task.Run(async () =>
            {
                try
                {
                    await _pipeClient.WaitForRemotePipeCloseAsync();
                    _pipeClient.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while waiting for remote pipe to close");
                }
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Pipe client initialization failed due to timeout");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipe client initialization failed");
            throw; // Optional: rethrow if you want the caller to handle it
        }
    }
    private async Task ConnectWithTimeout()
    {
        var connectTask = _pipeClient.ConnectAsync();
        if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(10))) == connectTask)
        {
            await connectTask; // Connection successful
        }
        else
        {
            throw new TimeoutException("Pipe client connection timed out");
        }
    }

    private async Task<string> GetPipeName()
    {
        /*var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        var parent = Directory.GetParent(exePath).Parent;
        var pipeFile = Path.Combine(parent.FullName, PipeFileName);
        */
        if (!File.Exists(_pipeFileName))
        {
#if DEBUG
            return $"DEBUG_PIPE-{Guid.NewGuid()}";
#endif
            _logger.LogInformation("No pipe name found, running standalone.");
            return null;
        }

        return await File.ReadAllTextAsync(_pipeFileName);
    }

    public void Dispose()
    {
        _pipeClient?.Dispose();
    }

    //Events
    private async Task PingServerOnInit()
    {
        try
        {
            await _pipeClient.InvokeAsync(x => x.OnClientInitialized());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Can't send ping server on init event");
        }
    }

    public async Task SendTrackPlayedEvent(TrackPlayedEvent e)
    {
        /*if (_pipeClient.State != PipeState.Connected)
        {
            _playerCore.SavePlayerHistory(e);
        };*/

        try
        {
            await _pipeClient.InvokeAsync(x => x.TrackPlayed(e));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Can't send track played event");
        }
    }
}