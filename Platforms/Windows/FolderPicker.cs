using Windows.Storage.Pickers;
using WindowsFolderPicker = Windows.Storage.Pickers.FolderPicker;
using WindowsFilePicker = Windows.Storage.Pickers.FileOpenPicker;

namespace VideoBlazor.Platforms.Windows
{
    public class FolderPicker : IFolderPicker
    {
        public async Task<string> PickFolder()
        {
            var folderPicker = new WindowsFolderPicker();

            // Get the current window's HWND by passing in the Window object
            var hwnd = ((MauiWinUIWindow)App.Current.Windows[0].Handler.PlatformView).WindowHandle;

            // Associate the HWND with the file picker
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var result = await folderPicker.PickSingleFolderAsync();

            return result?.Path;
        }

        public async Task<string> PickImageFile()
        {
            var filePicker = new WindowsFilePicker();
            filePicker.FileTypeFilter.Add(".jpg");
            filePicker.FileTypeFilter.Add(".jpeg");
            filePicker.FileTypeFilter.Add(".png");
            filePicker.FileTypeFilter.Add(".webp");
            filePicker.FileTypeFilter.Add(".svg");
            filePicker.FileTypeFilter.Add(".gif");

            // Get the current window's HWND by passing in the Window object
            var hwnd = ((MauiWinUIWindow)App.Current.Windows[0].Handler.PlatformView).WindowHandle;

            // Associate the HWND with the file picker
            WinRT.Interop.InitializeWithWindow.Initialize(filePicker, hwnd);

            var result = await filePicker.PickSingleFileAsync();

            return result?.Path;
        }
    }
}