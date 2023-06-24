using Microsoft.Maui.Storage;
using VideoBlazor.Domain.Models;

namespace VideoBlazor.Persistence;

public class SettingsRepository
{
    private readonly JsonRepository _repository = new JsonRepository();
    private readonly string _settingsFile = "settings.json";

    public SettingsRepository()
    {
        _repository.EnsureAppFolder();
        EnsureSettingsExist().ConfigureAwait(false);
    }

    public async Task<SettingsModel> GetSettings()
    {
        return await _repository.LoadJsonAsync<SettingsModel>(_settingsFile);
    }

    public async Task SaveSettings(SettingsModel settings)
    {
        await _repository.SaveJson(settings, _settingsFile);
    }
    
    public async Task EnsureSettingsExist()
    {
        if(!File.Exists(Path.Join(_repository.GetDataFolder(), _settingsFile)))
        {
            await SaveSettings(new SettingsModel());
        }
    }
}
