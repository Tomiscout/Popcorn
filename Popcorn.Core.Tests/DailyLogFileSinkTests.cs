using NUnit.Framework;
using Popcorn.Shared.Logging;
using Serilog;
using Serilog.Formatting.Display;

namespace Popcorn.Core.Tests;

public class DailyLogFileSinkTests
{
    private string _folder = null!;

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(Path.GetTempPath(), "popcorn-log-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void File_past_the_limit_restarts_with_last_events_and_a_notice()
    {
        using (var sink = new DailyLogFileSink(_folder, "Test",
                   new MessageTemplateTextFormatter(DailyLogFileSink.OutputTemplate), maxFileBytes: 4096))
        using (var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
        {
            for (var i = 0; i < 200; i++) logger.Information("Event {Number} with some padding text", i);
        }

        var path = Directory.GetFiles(_folder).Single();
        Assert.That(new FileInfo(path).Length, Is.LessThanOrEqualTo(4096));
        var lines = File.ReadAllLines(path);
        // The last truncation happened after some event n: the 10 events up to n, the notice, then the ones after it.
        var notice = Array.FindLastIndex(lines, l => l.Contains("This log file was truncated due to its size exceeding 4096 bytes at "));
        Assert.That(notice, Is.EqualTo(10));
        Assert.That(lines[notice], Does.Contain("[WRN]"));
        var numbers = lines.Where(l => l.Contains("[INF]")).Select(l => int.Parse(l.Split("Event ")[1].Split(' ')[0])).ToList();
        Assert.That(numbers, Is.EqualTo(Enumerable.Range(numbers[0], numbers.Count)));
        Assert.That(numbers[^1], Is.EqualTo(199));
    }

    [Test]
    public void Reopened_file_is_appended_to()
    {
        for (var run = 0; run < 2; run++)
        {
            using var sink = new DailyLogFileSink(_folder, "Test", new MessageTemplateTextFormatter(DailyLogFileSink.OutputTemplate));
            using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            logger.Information("Run {Run}", run);
        }

        var lines = File.ReadAllLines(Directory.GetFiles(_folder).Single());
        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Does.Contain("Run 0"));
        Assert.That(lines[1], Does.Contain("Run 1"));
    }
}
