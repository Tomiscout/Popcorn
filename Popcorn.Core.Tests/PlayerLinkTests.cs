using NUnit.Framework;
using Popcorn.Shared;

namespace Popcorn.Core.Tests;

public class PlayerLinkTests
{
    [Test]
    public async Task MessagesReachTheListener()
    {
        var received = new List<PlayerLinkMessage>();
        var gotBoth = new TaskCompletionSource();
        using var listener = PlayerLink.TryListen(m =>
        {
            lock (received)
            {
                received.Add(m);
                if (received.Count == 2) gotBoth.TrySetResult();
            }
            return Task.CompletedTask;
        });
        if (listener == null) Assert.Ignore("A player is running (it owns the socket)");

        var id = Guid.NewGuid();
        Assert.That(await PlayerLink.TrySendAsync(new PlayerLinkMessage(FileId: id)), Is.True);
        Assert.That(await PlayerLink.TrySendAsync(new PlayerLinkMessage(FilePath: "/tmp/a \"b\".mkv")), Is.True);
        await gotBoth.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(received, Is.EquivalentTo(new[]
        {
            new PlayerLinkMessage(FileId: id),
            new PlayerLinkMessage(FilePath: "/tmp/a \"b\".mkv"),
        }));
    }

    [Test]
    public async Task NoListenerMeansNotSent()
    {
        using (var listener = PlayerLink.TryListen(_ => Task.CompletedTask))
        {
            if (listener == null) Assert.Ignore("A player is running (it owns the socket)");
        }
        Assert.That(File.Exists(PlayerLink.SocketPath), Is.False);
        Assert.That(await PlayerLink.TrySendAsync(new PlayerLinkMessage(FileId: Guid.NewGuid())), Is.False);
    }
}
