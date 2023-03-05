using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Core.Exceptions;

namespace VideoBlazor.Core.Utils;

public static class FileService
{
    public static void LaunchMediaFile(string path)
    {
        try
        {
            var command = FileAssociation.GetExecFileAssociatedToExtension(Path.GetExtension(path));
            
            var arguments = new List<string>() { $"{path}" };
            if(command.EndsWith("vlc.exe"))
            {
                arguments.Add("--fullscreen");
                arguments.Add("--one-instance");
            }
            Process.Start(command, arguments);
        }
        catch (Exception e)
        {
            throw new UnableToOpenFileException(e.Message, e);
        }
    }
}
