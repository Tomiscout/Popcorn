using Popcorn.Domain.Models;

namespace Popcorn.Persistence;

public class SettingsRepository
{
    private readonly JsonRepository _repository = new JsonRepository();
    private readonly string _settingsFile = "settings.json";
    private static SettingsModel _cachedSettings;
    // Server and players share the file: another process' save is read again.
    private static DateTime _cachedWriteTime;

    public SettingsRepository()
    {
        _repository.EnsureAppFolder();
        EnsureSettingsExist().ConfigureAwait(false);
    }

    public async Task<SettingsModel> GetSettingsAsync()
    {
        var writeTime = File.GetLastWriteTimeUtc(SettingsPath);
        if(_cachedSettings is null || writeTime != _cachedWriteTime)
        {
            var settings = await _repository.LoadJsonAsync<SettingsModel>(_settingsFile);
            settings.NormalizePlayers();
            _cachedSettings = settings;
            _cachedWriteTime = writeTime;
        }
        return _cachedSettings;
    }

    public async Task SaveSettings(SettingsModel settings)
    {
        settings.NormalizePlayers();
        await _repository.SaveJsonAsync(settings, _settingsFile);
        _cachedSettings = settings;
        _cachedWriteTime = File.GetLastWriteTimeUtc(SettingsPath);
    }
    
    private string SettingsPath => Path.Join(_repository.GetDataFolder(), _settingsFile);

    public async Task EnsureSettingsExist()
    {
        if(!File.Exists(SettingsPath))
        {
            await SaveSettings(new SettingsModel { UseInternalPlayer = true }); // fresh install: Popcorn player first
        }
    }
}
