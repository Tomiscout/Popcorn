
namespace Popcorn.Shared.Utils;
public static class DirectoryUtils
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Popcorn" + Path.DirectorySeparatorChar);
    public static readonly string StreamCache = DataFolder + "StreamCache" + Path.DirectorySeparatorChar;

    public static void EnsureApplicationFolder()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(StreamCache);
    }
}
