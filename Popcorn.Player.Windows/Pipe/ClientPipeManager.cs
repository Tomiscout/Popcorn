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
        var pipeName = await GetPipeName();

        // Initialize the pipe client. Note that the type parameters are exactly
        // the same passed in the server example, but in reverse order.
        // The first type parameters indicates what the other process can do,
        // while the second one indicates the functionality this process exposes.
        _pipeClient = new PipeClientWithCallback<PopcornPlayer.Shared.IPipeServer, IPipeClient>(
            new NetJsonPipeSerializer(), pipeName, () => new PipeClient(_playerCore));

        // We wait until the connection is established
        await _pipeClient.ConnectAsync();

        await PingServerOnInit();

        await _pipeClient.WaitForRemotePipeCloseAsync();

        _pipeClient.Dispose();
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
            Console.WriteLine("No pipe name found! Exiting...");
            Environment.Exit(0);
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