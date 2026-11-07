using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Popcorn.Server.PluginHost;

/// <summary>Serves <c>/plugins/{id}/...</c> from the <c>web/</c> folder of the currently loaded plugin with that id.</summary>
public sealed class PluginWebFileProvider(PluginManager pluginManager) : IFileProvider
{
    public IFileInfo GetFileInfo(string subpath)
    {
        var path = subpath.TrimStart('/');
        var slash = path.IndexOf('/');
        if (slash <= 0) return new NotFoundFileInfo(subpath);

        var files = pluginManager.Get(path[..slash])?.WebFiles;
        return files?.GetFileInfo(path[slash..]) ?? new NotFoundFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
}
