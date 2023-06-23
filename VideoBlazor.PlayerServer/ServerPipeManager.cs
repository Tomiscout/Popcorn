using PipeMethodCalls;
using PipeMethodCalls.NetJson;
using System.Diagnostics;
using VideoPlayer.Shared;

namespace VideoBlazor.PlayerServer
{
    public static class ServerPipeManager
    {
        private static readonly string _pipeNameFile = @"C:\Projects\VideoBlazor\pipeName.txt";
        
        public static async Task StartServer()
        {
            var pipeName = GeneratePipeName();
            await File.WriteAllTextAsync(_pipeNameFile, pipeName);

            var pipeServer = new PipeServerWithCallback<IPlayerPipeClient, IAppPlayerServer>(
                new NetJsonPipeSerializer(), pipeName, () => new AppPlayerServer());

            // We wait for a connection (this could be a long time).
            await pipeServer.WaitForConnectionAsync();
            // If execution continues, that means we have a connection.
            Debug.WriteLine("Pipeline connected.");
            // Delete the pipename file, since we don't need it anymore
            File.Delete(_pipeNameFile);

            // Wait until somebody closes the pipe.
            await pipeServer.WaitForRemotePipeCloseAsync();
            // Clean up
            pipeServer.Dispose();
            Debug.WriteLine("Pipeline disconnected.");
            
        }

        private static string GeneratePipeName () => $"VideoBlazor-{Guid.NewGuid()}";
    }
}