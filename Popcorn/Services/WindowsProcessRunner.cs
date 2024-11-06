using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MonoTorrent;
using Popcorn.Domain.Interfaces;

namespace Popcorn.Services;

public class WindowsProcessRunner : INativeProcessRunner
{
    public void ExecuteCommandDetached(string command, string args = "")
    {

        int firstSpaceIndex = command.IndexOf(' ', command.IndexOf(".exe") + 4);

        // Separate the path and arguments
        string exePath = command.Substring(0, firstSpaceIndex);
        string arguments = command.Substring(firstSpaceIndex + 1);

        var processStartInfo = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath)
        };
        Process.Start(processStartInfo);
    }

    public string ExecuteCommandRead(string command, string args = "")
    {
        throw new NotImplementedException();
    }
}
