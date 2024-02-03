using PipeMethodCalls;
using PipeMethodCalls.NetJson;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Serilog;
using Popcorn.Domain.Interfaces;
using Popcorn.Domain.Models;
using Popcorn.Domain.Services;
using Popcorn.Shared.Utils;
using PopcornPlayer.Shared;
using PopcornPlayer.Shared.PipeEvents.Client;

namespace Popcorn.PlayerServer
{
    public static class ServerPipeManager
    {
        private static readonly string _pipeNameFile = DirUtils.DataFolder + "pipeName.txt";
        private static PipeServerWithCallback<IPipeClient, PopcornPlayer.Shared.IPipeServer> _pipeServer;

        public static async Task StartServer(MediaItem initialItem, IMediaItemRepository mediaItemRepository, MediaItemRefreshService mediaItemRefreshService)
        {
            try
            {
                var pipeName = GeneratePipeName();
                await File.WriteAllTextAsync(_pipeNameFile, pipeName);

                _pipeServer = new PipeServerWithCallback<IPipeClient, PopcornPlayer.Shared.IPipeServer>(
                    new NetJsonPipeSerializer(), pipeName, () => new PipeServer(initialItem, mediaItemRepository, mediaItemRefreshService));

                // We wait for a connection (this could be a long time).
                await EnsureConnection();
                // If execution continues, that means we have a connection.
                Log.Logger.Information("[Server PipeManager] Pipeline connected.");
                // Delete the pipename file, since we don't need it anymore
                File.Delete(_pipeNameFile);

                // Wait until somebody closes the pipe.
                await _pipeServer.WaitForRemotePipeCloseAsync();
                // Clean up
                _pipeServer.Dispose();
                Log.Logger.Information("[Server PipeManager] Pipeline disconnected.");
            }
            catch (Exception e)
            {
                Log.Logger.Error(e, "[Server PipeManager] Crash!");
            }
        }

        private static string GeneratePipeName () => $"Popcorn-{Guid.NewGuid()}";
        
        public static bool IsConnected() => _pipeServer != null && _pipeServer.State == PipeState.Connected;

        private static AsyncTimeoutPolicy _listenerTimeoutPolicy = Policy.TimeoutAsync(
        TimeSpan.FromSeconds( 10 ),
        TimeoutStrategy.Optimistic);

        private static RetryPolicy _isConnectedRetryPolicy = Policy.Handle<Exception>().WaitAndRetry(100, _ => TimeSpan.FromMilliseconds(100));

        private static async Task EnsureConnection()
        {
            if (IsConnected()) return;
            
            var policyResult = await _listenerTimeoutPolicy.ExecuteAndCaptureAsync(_pipeServer.WaitForConnectionAsync, CancellationToken.None);
            if(policyResult.FinalException != null)
            {
                throw policyResult.FinalException;
            }
        }

        public static async Task LoadMediaEvent(string path) => await _pipeServer.InvokeAsync(x => x.LoadMedia(new LoadMediaEvent() { FilePath = path }));

        public static async Task SendMediaInfoEvent(MediaItem item) => await _pipeServer.InvokeAsync(x => x.MediaInfo(item));
        public static async Task UpdateMediaItemHistory(List<ItemHistory> item) => await _pipeServer.InvokeAsync(x => x.UpdateMediaItemHistory(item));
        
        public static void Dispose()
        {
            _pipeServer?.Dispose();
        }

        public static void GracefullDispose()
        {
            _pipeServer.InvokeAsync(x => x.Kill()).RunSynchronously();
            _pipeServer?.Dispose();
        }
    }
}