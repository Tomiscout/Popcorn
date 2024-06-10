
namespace Popcorn.Shared.Utils;
public class DirUtils
{
    public static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Popcorn" + Path.DirectorySeparatorChar);

    public static void EnsureApplicationFolder()
    {
        Directory.CreateDirectory(DataFolder);
    }
}
