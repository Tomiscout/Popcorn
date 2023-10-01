using System.Collections.Concurrent;
using System.Text.Json;
using VideoBlazor.Shared.Utils;

namespace VideoBlazor.Persistence
{
    public class JsonRepository //AI generated the locks, let's see how it works out :D
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public JsonRepository()
        {
            EnsureAppFolder();
        }

        public string GetDataFolder()
        {
            return DirUtils.DataFolder;
        }

        public async Task<T> LoadJsonAsync<T>(string fileName)
        {
            var lockObject = _locks.GetOrAdd(fileName, new SemaphoreSlim(1, 1));
            await lockObject.WaitAsync();
            try
            {
                return await LoadJsonFullpathAsync<T>(Path.Join(GetDataFolder(), fileName));
            }
            finally
            {
                lockObject.Release();
            }
        }

        private async Task<T> LoadJsonFullpathAsync<T>(string fileName)
        {
            if (!File.Exists(fileName))
            {
                throw new FileNotFoundException($"File not found: {fileName}");
            }
            var json = await File.ReadAllTextAsync(fileName);
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SaveJsonAsync(object data, string fileName)
        {
            var lockObject = _locks.GetOrAdd(fileName, new SemaphoreSlim(1, 1));
            await lockObject.WaitAsync();
            try
            {
                var jsonString = JsonSerializer.Serialize(data);
                var path = Path.Join(GetDataFolder(), fileName);
                await File.WriteAllTextAsync(path, jsonString);
            }
            finally
            {
                lockObject.Release();
            }
        }

        public void EnsureAppFolder()
        {
            Directory.CreateDirectory(GetDataFolder());
        }
    }
}
