using PipeMethodCalls;
using PipeMethodCalls.NetJson;
using System;
using System.IO;
using System.Threading.Tasks;
using PopcornPlayer.Shared;
using PopcornPlayer.Shared.PipeEvents.Server;
using Serilog;
using Popcorn.Shared.Utils;
using PopcornPlayer.Core;

namespace PopcornPlayer.Pipes
{
    public static class PipeManager
    {
        private static readonly string _pipeFileName = DirUtils.DataFolder + "pipeName.txt";
        
        private static PipeClientWithCallback<Shared.IPipeServer, IPipeClient> pipeClient;

        public static async Task Initialize(MainWindow window)
        {
            await ConnectToAppPipe(window);
        }

        public static async Task ConnectToAppPipe(MainWindow window)
        {
            var pipeName = await GetPipeName();

            // Initialize the pipe client. Note that the type parameters are exactly
            // the same passed in the server example, but in reverse order.
            // The first type parameters indicates what the other process can do,
            // while the second one indicates the functionality this process exposes.
            pipeClient = new PipeClientWithCallback<Shared.IPipeServer, IPipeClient>(
                new NetJsonPipeSerializer(), pipeName, () => new PipeClient(window));

            // We wait until the connection is established
            await pipeClient.ConnectAsync();

            await PingServerOnInit();

            await pipeClient.WaitForRemotePipeCloseAsync();

            pipeClient.Dispose();
        }

        private static async Task<string> GetPipeName()
        {
            /*var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var parent = Directory.GetParent(exePath).Parent;
            var pipeFile = Path.Combine(parent.FullName, PipeFileName);
            */
            if(!File.Exists(_pipeFileName))
            {
#if DEBUG
                return $"DEBUG_PIPE-{Guid.NewGuid()}";
#endif
                Console.WriteLine("No pipe name found! Exiting...");
                Environment.Exit(0);
            }

            return await File.ReadAllTextAsync(_pipeFileName);
        }

        private sealed class Destructor
        {
            ~Destructor()
            {
                Dispose();
            }
        }

        public static void Dispose()
        {
            pipeClient?.Dispose();
        }

        //Events
        private static async Task PingServerOnInit()
        {
            try
            {
                await pipeClient.InvokeAsync(x => x.OnClientInitialized());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Can't send ping server on init event");
            }
        }

        public static async Task SendTrackPlayedEvent(TrackPlayedEvent e)
        {
            if (pipeClient.State != PipeState.Connected) {
                PlayerCore.SavePlayerHistory(e);
            };

            try
            {
                await pipeClient.InvokeAsync(x => x.TrackPlayed(e));
            } catch(Exception ex)
            {
                Log.Error(ex, "Can't send track played event");
                PlayerCore.SavePlayerHistory(e);
            }
        }
    }
}
