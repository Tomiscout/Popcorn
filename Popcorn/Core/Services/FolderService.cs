using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Maui.Storage;
using Popcorn.Core.Model;
using Popcorn.Persistence;
using Serilog;

namespace Popcorn.Core.Services
{
    public class FolderService //TODO INativeFolderService
    {
        private static readonly string _jsonName = "folders.json";
        private readonly IFolderPicker _folderPicker;
        private readonly JsonRepository _jsonRepository;

        public FolderService(IFolderPicker folderPicker, JsonRepository jsonRepository)
        {
            _folderPicker = folderPicker;
            _jsonRepository = jsonRepository;
        }

        public async Task SaveFolders(IList<Folder> folders)
        {
            await _jsonRepository.SaveJsonAsync(folders, _jsonName);
        }

        public async Task<IList<Folder>> LoadSavedFolders()
        {
            try
            {
                return await _jsonRepository.LoadJsonAsync<IList<Folder>>(_jsonName);
            } catch(FileNotFoundException)
            {
                var emptyList = new List<Folder>();
                await _jsonRepository.SaveJsonAsync(emptyList, _jsonName);
                return emptyList;
            }
        }

        public async Task DeleteFolder(string path)
        {
            var folders = await LoadSavedFolders();
            folders.Remove(folders.FirstOrDefault(x => x.Path == path));
            await _jsonRepository.SaveJsonAsync(folders, _jsonName);
        }

        public async Task<string> OpenFolderPicker()
        {
#pragma warning disable CA1416 // Validate platform compatibility
            var result = await _folderPicker.PickAsync(CancellationToken.None);
#pragma warning restore CA1416 // Validate platform compatibility
            return result.IsSuccessful ? result.Folder.Path : null;
        }

        public async Task<string> PickImageFile()
        {
            PickOptions options = new()
            {
                PickerTitle = "Please select an image file",
                FileTypes = FilePickerFileType.Images,
            };

            try
            {
                var result = await FilePicker.Default.PickAsync(options);
                return result?.FullPath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to pick a file");
            }

            return null;
        }

        public void OpenFolderInExplorer(string path)
        {
            Process.Start("explorer.exe", path);
        }

        public void OpenRootFolderExplorer()
        {
            OpenFolderInExplorer(_jsonRepository.GetDataFolder());
        }
    }
}
