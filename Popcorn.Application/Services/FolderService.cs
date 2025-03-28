using System.Diagnostics;
using System.Threading;
using Popcorn.Core.Model;
using Popcorn.Persistence;

namespace Popcorn.Core.Services
{
    public class FolderService
    {
        private static readonly string _jsonName = "folders.json";
        private readonly JsonRepository _jsonRepository;

        public FolderService(JsonRepository jsonRepository)
        {
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
    }
}
