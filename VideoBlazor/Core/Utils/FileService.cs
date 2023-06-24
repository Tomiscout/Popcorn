using System.Diagnostics;
using VideoBlazor.Core.Exceptions;
using VideoBlazor.Shared;

namespace VideoBlazor.Core.Utils;

public static class FileService
{
    public static void LaunchMediaFileDefault(string path)
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
