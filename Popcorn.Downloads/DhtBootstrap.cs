using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MonoTorrent.BEncoding;
using MonoTorrent.Dht;

namespace Popcorn.Downloads;

/// <summary>
/// Gets MonoTorrent 3.0.2's DHT off the ground. On its own it never bootstraps here:
/// <list type="bullet">
/// <item>its only bootstrap router is router.bittorrent.com:6881, which does not answer (nor does router.utorrent.com);</item>
/// <item>its node cache (<c>dht_nodes.cache</c>) is saved as a bencoded list of compact nodes but loaded as raw
/// 26-byte compact nodes, so saved nodes come back as garbage.</item>
/// </list>
/// <see cref="PrepareAsync"/> runs before the engine starts: it rewrites the cache as raw compact nodes (saved nodes
/// kept, the routers added) and replaces MonoTorrent's bootstrap list (private static, set by reflection), which is
/// used whenever the routing table is still empty, e.g. after the engine restarts in-process and reloads its own
/// list-format cache.
/// </summary>
internal static class DhtBootstrap
{
    public const string CacheFileName = "dht_nodes.cache";
    private const int CompactNodeLength = 26;
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(5);

    private static readonly (string Host, int Port)[] Routers =
    [
        ("dht.transmissionbt.com", 6881),
        ("dht.libtorrent.org", 25401),
        ("router.utorrent.com", 6881),
        ("router.bittorrent.com", 6881),
    ];

    public static async Task PrepareAsync(string cacheDirectory, ILogger logger)
    {
        try
        {
            var routers = await ResolveRoutersAsync(logger);
            var path = Path.Combine(cacheDirectory, CacheFileName);
            var saved = File.Exists(path) ? ReadCache(await File.ReadAllBytesAsync(path)) : [];

            Directory.CreateDirectory(cacheDirectory);
            await File.WriteAllBytesAsync(path, [.. saved.SelectMany(n => n), .. routers.SelectMany(n => n)]);
            SetBootstrapNodes(routers, logger);
            logger.LogInformation("DHT bootstrap: {Saved} saved nodes, {Routers} routers", saved.Count, routers.Count);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not prepare the DHT bootstrap nodes");
        }
    }

    /// <summary>Saved nodes in MonoTorrent's list format, or the raw format this class writes.</summary>
    private static List<byte[]> ReadCache(byte[] data)
    {
        if (data.Length == 0) return [];
        if (data[0] == (byte)'l')
        {
            try
            {
                return BEncodedValue.Decode<BEncodedList>(data)
                    .OfType<BEncodedString>()
                    .Select(s => s.AsMemory().ToArray())
                    .Where(b => b.Length == CompactNodeLength)
                    .ToList();
            }
            catch (BEncodingException)
            {
                return [];
            }
        }
        return data.Length % CompactNodeLength == 0 ? data.Chunk(CompactNodeLength).ToList() : [];
    }

    private static async Task<List<byte[]>> ResolveRoutersAsync(ILogger logger)
    {
        using var timeout = new CancellationTokenSource(DnsTimeout);
        var lookups = Routers.Select(async router =>
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(router.Host, AddressFamily.InterNetwork, timeout.Token);
                return addresses.Select(a => CompactNode(a, router.Port));
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException)
            {
                logger.LogDebug("DHT router {Host} not resolved: {Error}", router.Host, e.Message);
                return [];
            }
        });
        return (await Task.WhenAll(lookups)).SelectMany(n => n).ToList();
    }

    /// <summary>20-byte random node id, IPv4 address, big-endian port.</summary>
    private static byte[] CompactNode(IPAddress address, int port)
    {
        var node = new byte[CompactNodeLength];
        Random.Shared.NextBytes(node.AsSpan(0, 20));
        address.GetAddressBytes().CopyTo(node, 20);
        node[24] = (byte)(port >> 8);
        node[25] = (byte)port;
        return node;
    }

    /// <summary><c>MonoTorrent.Dht.Tasks.InitialiseTask.BootstrapNodes</c> (a <c>Node[]</c>), built with the internal
    /// <c>Node.FromCompactNode(BEncodedString)</c>. Skipped with a warning if a MonoTorrent update renamed them.</summary>
    private static void SetBootstrapNodes(List<byte[]> routers, ILogger logger)
    {
        if (routers.Count == 0) return;
        var assembly = typeof(DhtEngine).Assembly;
        var nodeType = assembly.GetType("MonoTorrent.Dht.Node");
        var property = assembly.GetType("MonoTorrent.Dht.Tasks.InitialiseTask")
            ?.GetProperty("BootstrapNodes", BindingFlags.Static | BindingFlags.NonPublic);
        var fromCompact = nodeType?.GetMethod("FromCompactNode", BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(BEncodedString)]);
        if (nodeType is null || property is null || fromCompact is null)
        {
            logger.LogWarning("MonoTorrent's DHT bootstrap list not found; DHT restarts fall back to its own router");
            return;
        }

        var nodes = ((System.Collections.IEnumerable)fromCompact.Invoke(null, [new BEncodedString(routers.SelectMany(n => n).ToArray())])!)
            .Cast<object>().ToArray();
        var array = Array.CreateInstance(nodeType, nodes.Length);
        Array.Copy(nodes, array, nodes.Length);
        property.SetValue(null, array);
    }
}
