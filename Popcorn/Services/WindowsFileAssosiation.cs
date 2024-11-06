using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Popcorn.Services;

/// <summary>
/// Usage: string executablePath = FileAssociation.GetExecFileAssociatedToExtension(pathExtension, "open");
/// Usage: string command FileAssociation.GetExecCommandAssociatedToExtension(pathExtension, "open");
/// </summary>
public class WindowsFileAssosiation : INativeFileAssosiation
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="ext"></param>
    /// <returns>Return null if not found</returns>
    public string GetExecCommandForExtention(string ext, string filePath, string args)
    {
        if (ext[0] != '.')
        {
            ext = "." + ext;
        }

        var exePaths = RecommendedPrograms(ext);
        if (exePaths.Count == 0) return null;

        var exePath = exePaths.First();

        if (exePath.ToLower().EndsWith("vlc.exe"))
        {
            exePath += " --fullscreen --one-instance";
        }
        return exePath + " " + args;
    }

    // https://stackoverflow.com/a/66651052
    public List<string> RecommendedPrograms(string ext)
    {
        //Search programs names:
        List<string> names = new List<string>();
        string baseKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + ext;
        string s;

        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(baseKey + @"\OpenWithList"))
        {
            if (rk != null)
            {
                string mruList = (string)rk.GetValue("MRUList");
                if (mruList != null)
                {
                    foreach (char c in mruList)
                    {
                        s = rk.GetValue(c.ToString()).ToString();
                        if (s.ToLower().Contains(".exe"))
                            names.Add(s);
                    }
                }
            }
        }

        if (names.Count == 0)
            return names;

        //Search paths:
        List<string> paths = new List<string>();
        baseKey = @"Software\Classes\Applications\{0}\shell\open\command";

        foreach (string name in names)
            using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(String.Format(baseKey, name)))
            {
                if (rk != null)
                {
                    s = rk.GetValue("").ToString();
                    s = s.Substring(1, s.IndexOf("\"", 2) - 1); //remove quotes
                    paths.Add(s);
                }
            }

        if (paths.Count > 0)
            return paths;

        //Search pathes for Windows XP:
        foreach (string name in names)
            using (RegistryKey rk = Registry.LocalMachine.OpenSubKey(String.Format(baseKey, name)))
            {
                if (rk != null)
                {
                    s = rk.GetValue("").ToString();
                    s = s.Substring(1, s.IndexOf("\"", 2) - 1); //remove quotes
                    paths.Add(s);
                }
            }

        return paths;
    }
}