using System.Text.Json;

namespace VideoBlazor.Core
{
    public static class JsonUtils
    {
        private static readonly string _rootFolder = FileSystem.Current.AppDataDirectory;
        public static string GetDataFolder()
        {
            return _rootFolder;
        }

        public static async Task<T> LoadJsonAsync<T>(string fileName)
        {
            return await LoadJsonFullpathAsync<T>(Path.Join(GetDataFolder(), fileName));
        }

        private static async Task<T> LoadJsonFullpathAsync<T>(string fileName)
        {
            if(!File.Exists(fileName))
            {
                throw new FileNotFoundException(fileName);
            }
            var json = await File.ReadAllTextAsync(fileName);
            return JsonSerializer.Deserialize<T>(json);
        }

        public static async Task SaveJson(object data, string fileName)
        {
            var jsonString = JsonSerializer.Serialize(data);
            var path = Path.Join(GetDataFolder(), fileName);
            await File.WriteAllTextAsync(path, jsonString);
        }

        public static void EnsureAppFolder()
        {
            Directory.CreateDirectory(GetDataFolder());
        }
    }
}
