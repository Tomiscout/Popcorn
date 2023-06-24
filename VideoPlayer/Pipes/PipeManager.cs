using PipeMethodCalls;
using PipeMethodCalls.NetJson;
using System;
using System.IO;
using System.Threading.Tasks;
using VideoPlayer.Shared;
using WebSiteRatings;

namespace VideoPlayer.Pipes
{
    public static class PipeManager
    {
        //private static readonly string PipeFileName = "pipeName.txt";
        private static readonly string _pipeFileName = @"C:\Projects\VideoBlazor\pipeName.txt";
        
        private static PipeClientWithCallback<IAppPlayerServer, IPlayerPipeClient> pipeClient;

        public static Action<string> Print;
        
        public static async Task Initialize(MainWindow window)
        {
            await ConnectToAppPipe(window);
        }

        public static void Test()
        {
            
            Print.Invoke($"Test");
        }

        public static async Task ConnectToAppPipe(MainWindow window)
        {
            var pipeName = await GetPipeName();

            // Initialize the pipe client. Note that the type parameters are exactly
            // the same passed in the server example, but in reverse order.
            // The first type parameters indicates what the other process can do,
            // while the second one indicates the functionality this process exposes.
            pipeClient = new PipeClientWithCallback<IAppPlayerServer, IPlayerPipeClient>(
                new NetJsonPipeSerializer(), pipeName, () => new PlayerPipeClient(window));

            // We wait until the connection is established
            await pipeClient.ConnectAsync();

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
                //TODO test
                pipeClient?.Dispose();
            }
        }
    }
}
