using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoBlazor.Core.Model;
using VideoBlazor.Pages;
using VideoBlazor.Platforms.Windows;

namespace VideoBlazor.Core.Services
{
    public class FolderService
    {
        private static readonly string _jsonName = "folders.json";
        private readonly IFolderPicker _folderPicker;

        public FolderService(IFolderPicker folderPicker)
        {
            _folderPicker = folderPicker;
        }

        public async Task SaveFolders(IList<Folder> folders)
        {
            await JsonUtils.SaveJson(folders, _jsonName);
        }

        public async Task<IList<Folder>> LoadSavedFolders()
        {
            try
            {
                return await JsonUtils.LoadJsonAsync<IList<Folder>>(_jsonName);
            } catch(FileNotFoundException)
            {
                var emptyList = new List<Folder>();
                await JsonUtils.SaveJson(emptyList, _jsonName);
                return emptyList;
            }
        }

        public async Task DeleteFolder(string path)
        {
            var folders = await LoadSavedFolders();
            folders.Remove(folders.FirstOrDefault(x => x.Path == path));
            await JsonUtils.SaveJson(folders, _jsonName);
        }

        public async Task<string> OpenFolderPicker()
        {
            return await _folderPicker.PickFolder();
        }

        public void OpenRootFolderExplorer()
        {
            Process.Start("explorer.exe", JsonUtils.GetDataFolder());
        }
    }
}
