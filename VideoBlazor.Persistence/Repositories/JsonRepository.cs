using System.Text.Json;

namespace VideoBlazor.Persistence
{
    public class JsonRepository
    {
        private readonly string _rootFolder = FileSystem.Current.AppDataDirectory;

        public JsonRepository()
        {
            EnsureAppFolder();
        }
        
        public string GetDataFolder()
        {
            return _rootFolder;
        }

        public async Task<T> LoadJsonAsync<T>(string fileName)
        {
            return await LoadJsonFullpathAsync<T>(Path.Join(GetDataFolder(), fileName));
        }

        private async Task<T> LoadJsonFullpathAsync<T>(string fileName)
        {
            if(!File.Exists(fileName))
            {
                throw new FileNotFoundException(fileName);
            }
            var json = await File.ReadAllTextAsync(fileName);
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SaveJson(object data, string fileName)
        {
            var jsonString = JsonSerializer.Serialize(data);
            var path = Path.Join(GetDataFolder(), fileName);
            await File.WriteAllTextAsync(path, jsonString);
        }

        public void EnsureAppFolder()
        {
            Directory.CreateDirectory(GetDataFolder());
        }
    }
}
