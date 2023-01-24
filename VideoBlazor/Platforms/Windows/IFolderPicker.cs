
namespace VideoBlazor.Platforms.Windows
{
    public interface IFolderPicker
    {
        Task<string> PickFolder();
        Task<string> PickImageFile();
    }
}
