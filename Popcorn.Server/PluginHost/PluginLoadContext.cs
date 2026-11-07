using System.Reflection;
using System.Runtime.Loader;

namespace Popcorn.Server.PluginHost;

/// <summary>
/// One per plugin: resolves the plugin's own dependencies from its folder (via its .deps.json), so plugins can ship
/// any library version. Collectible, so a plugin can be disabled/replaced at runtime. Contract assemblies are always taken from the server, otherwise the plugin's
/// IMediaSourcePlugin / ILogger would be different types than the server's.
/// </summary>
public sealed class PluginLoadContext(string mainAssemblyPath)
    : AssemblyLoadContext(Path.GetFileName(mainAssemblyPath), isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    private static bool IsShared(AssemblyName name) =>
        name.Name is { } n && (n == typeof(Popcorn.Plugins.IMediaSourcePlugin).Assembly.GetName().Name
                               || n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName)) return null; // default context = the server's copy

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path); // null: framework assembly, from the default context
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
