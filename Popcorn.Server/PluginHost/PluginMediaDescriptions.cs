using Popcorn.Domain.Interfaces;
using Popcorn.FilenameParser;
using Popcorn.Plugins;

namespace Popcorn.Server.PluginHost;

/// <summary>
/// Description of a new library item: the overview of the best search match of the first media source plugin (in the
/// user's plugin order) that has one. Items are created while this runs, so the whole lookup has a short time limit.
/// </summary>
public sealed class PluginMediaDescriptions(PluginManager pluginManager, ILogger<PluginMediaDescriptions> logger) : IMediaDescriptionProvider
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> FindDescriptionAsync(string name)
    {
        var parsed = MediaNameParser.Parse(name);
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? name : parsed.Title;
        var year = parsed.Year?.ToString();

        using var cts = new CancellationTokenSource(Timeout);
        foreach (var plugin in pluginManager.Plugins.Where(p => p.Media != null))
        {
            try
            {
                var cards = await plugin.Media!.SearchAsync(title, cts.Token);
                var matches = cards.Where(c => c.HasDetails).ToList();
                var best = matches.FirstOrDefault(c => year != null && c.Subtitle?.Contains(year) == true) ?? matches.FirstOrDefault();
                if (best == null) continue;

                var details = await plugin.Media.GetDetailsAsync(best.Id, cts.Token);
                if (!string.IsNullOrWhiteSpace(details?.Overview)) return details.Overview.Trim();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Plugin {Plugin} could not describe {Title}", plugin.Manifest.Id, title);
            }
        }
        return null;
    }
}
