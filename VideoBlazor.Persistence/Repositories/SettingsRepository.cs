using Microsoft.Maui.Storage;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.Persistence;

public class SettingsRepository
{
    private readonly JsonRepository _repository = new JsonRepository();
    private readonly string _settingsFile = "settings.json";
    private static SettingsModel _cachedSettings;

    public SettingsRepository()
    {
        _repository.EnsureAppFolder();
        EnsureSettingsExist().ConfigureAwait(false);
    }

    public async Task<SettingsModel> GetSettings()
    {
        if(_cachedSettings is null)
        {
            _cachedSettings = await _repository.LoadJsonAsync<SettingsModel>(_settingsFile);
        }
        return _cachedSettings;
    }

    public async Task SaveSettings(SettingsModel settings)
    {
        await _repository.SaveJson(settings, _settingsFile);
        _cachedSettings = settings;
    }
    
    public async Task EnsureSettingsExist()
    {
        if(!File.Exists(Path.Join(_repository.GetDataFolder(), _settingsFile)))
        {
            await SaveSettings(new SettingsModel());
        }
    }
}
