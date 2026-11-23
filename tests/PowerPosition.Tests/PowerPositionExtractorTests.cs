using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Domain;
using PowerPosition.Service.Extraction;
using PowerPosition.Service.Output;

namespace PowerPosition.Tests;

public sealed class PowerPositionExtractorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PowerPositionTests", Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Requirement", "2")]
    [Trait("Requirement", "3")]
    [Trait("Requirement", "4")]
    [Trait("Requirement", "5")]
    public async Task ExtractAsync_SpecExample_WritesExpectedCsvNamedByLocalExtractTime()
    {
        var date = new DateTime(2015, 4, 2);
        var service = new TestData.FakePowerService(_ => TestData.SpecExampleTrades(date));
        var extractor = CreateExtractor(service);

        var path = await extractor.ExtractAsync(new DateTimeOffset(2015, 4, 1, 17, 37, 12, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal([date], service.RequestedDates);
        Assert.Equal(Path.Combine(_directory, "PowerPosition_20150401_1837.csv"), path);
        Assert.Equal(TestData.SpecExampleCsv, await File.ReadAllTextAsync(path));
    }

    [Fact]
    [Trait("Requirement", "DayAhead")]
    [Trait("Requirement", "4")]
    public async Task ExtractAsync_AfterLocalMidnight_RequestsFollowingDay()
    {
        var service = new TestData.FakePowerService(_ => []);
        var extractor = CreateExtractor(service);

        var path = await extractor.ExtractAsync(new DateTimeOffset(2015, 4, 1, 23, 30, 0, TimeSpan.Zero), CancellationToken.None);

        Assert.Equal([new DateTime(2015, 4, 3)], service.RequestedDates);
        Assert.EndsWith("PowerPosition_20150402_0030.csv", path);
    }

    private PowerPositionExtractor CreateExtractor(TestData.FakePowerService service)
    {
        var calendar = new TradingCalendar(TestData.London);
        return new PowerPositionExtractor(
            service,
            calendar,
            new PositionAggregator(calendar, NullLogger<PositionAggregator>.Instance),
            new PowerPositionCsvWriter(),
            Options.Create(new PowerPositionOptions { OutputDirectory = _directory }),
            NullLogger<PowerPositionExtractor>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
