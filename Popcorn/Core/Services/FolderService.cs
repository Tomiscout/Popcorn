using System.Diagnostics;
using Popcorn.Core.Model;
using Popcorn.Persistence;
using Popcorn.Platforms.Windows;

namespace Popcorn.Core.Services
{
    public class FolderService
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
            return await _folderPicker.PickFolder();
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
