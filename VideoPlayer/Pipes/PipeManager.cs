using H.Pipes;
using H.Pipes.Args;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VideoPlayer.Pipes.PipeEvents;
using WebSiteRatings;

namespace VideoPlayer.Pipes
{
    public static class PipeManager
    {
        public static readonly string PipeName = "BlazorVideoPlayer";
        //private static ILogger logger = new Logger<PipeManager>();
        private static PipeServer<PipeEvent> _server;

        public static async Task Initialize(string[] args)
        {
            if (args.Length > 0 && DoesPipeExist())
            {
                if (await PipeManager.PassMediaToRunningInstance(args[0]))
                {
                    Environment.Exit(0);
                }
            }
            else
            {
                await CreateServer();
            }
        }

        public static void Test()
        {
            var running = _server?.IsStarted;
            var clients = _server?.ConnectedClients;
        }

        public static async Task CreateServer()
        {
            
            _server = new PipeServer<PipeEvent>(PipeName);
            /*server.ClientDisconnected += (o, args) =>
            {
                Console.WriteLine($"Client {args.Connection.PipeName} disconnected");
            };*/
            _server.ExceptionOccurred += (o, args) =>
            {
                var e = args.Exception;
            };

            _server.MessageReceived += MessageReceivedHandler;

            await _server.StartAsync();
            
        }

        private static void MessageReceivedHandler(object sender, ConnectionMessageEventArgs<PipeEvent> args)
        {
            if(args.Message.EventName == "LoadedMedia")
            {
                ((MainWindow)App.Current.Windows[0]).LoadMedia(((LoadedMediaEvent)args.Message).FilePath);
            }
        }

        public static async Task<bool> PassMediaToRunningInstance(string filePath)
        {
            try
            {
                await using var client = new PipeClient<LoadedMediaEvent>(PipeName);
                client.ExceptionOccurred += (o, args) =>
                {
                    var e = args.Exception;
                };
                await client.ConnectAsync();
                await client.WriteAsync(new LoadedMediaEvent
                {
                    EventName = "LoadedMedia",
                    FilePath = filePath,
                });
                return true;
            } catch (Exception e)
            {
                //Failed to connect
                return false;
            }
           
        }

        //Refer to https://berserkerdotnet.github.io/blog/detecting-namedpipe-exists/
        private static bool DoesPipeExist()
        {
            return File.Exists($"\\\\.\\pipe\\{PipeName}");
        }

        
        private sealed class Destructor
        {
            ~Destructor()
            {
                //TODO test
                _server.StopAsync().Wait();
                _server.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
