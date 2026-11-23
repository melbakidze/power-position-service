using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Domain;
using PowerPosition.Service.Extraction;
using PowerPosition.Service.Output;
using PowerPosition.Service.Scheduling;
using Services;

namespace PowerPosition.Tests;

public sealed class LoggingTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PowerPositionTests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2015, 4, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    [Trait("Requirement", "10")]
    public async Task Extractor_LogsTradingDateTradeCountAndOutputPath()
    {
        var date = new DateTime(2015, 4, 2);
        var service = new TestData.FakePowerService(_ => TestData.SpecExampleTrades(date));
        var calendar = new TradingCalendar(TestData.London);
        var logger = new FakeLogger<PowerPositionExtractor>();
        var extractor = new PowerPositionExtractor(
            service,
            calendar,
            new PositionAggregator(calendar, NullLogger<PositionAggregator>.Instance),
            new PowerPositionCsvWriter(),
            Options.Create(new PowerPositionOptions { OutputDirectory = _directory }),
            logger);

        var path = await extractor.ExtractAsync(new DateTimeOffset(2015, 4, 1, 17, 37, 0, TimeSpan.Zero), CancellationToken.None);
        var records = logger.Collector.GetSnapshot();

        Assert.Contains(records, record => record.Message.Contains("2015-04-02") && record.Message.Contains("2015-04-01 18:37"));
        Assert.Contains(records, record => record.Message.Contains("Retrieved 2 trades"));
        Assert.Contains(records, record => record.Message.Contains("Wrote 24 periods") && record.Message.Contains(path));
    }

    [Fact]
    [Trait("Requirement", "10")]
    public void Aggregator_LogsWarningForEverySkippedTradeOrPeriod()
    {
        var date = new DateTime(2015, 4, 1);
        var malformed = TestData.Trade(date, 1, 2);
        malformed.Periods[1].Period = 99;
        var logger = new FakeLogger<PositionAggregator>();
        var aggregator = new PositionAggregator(new TradingCalendar(TestData.London), logger);

        aggregator.Aggregate(date, [malformed, TestData.Trade(date.AddDays(1), 1)]);

        var warnings = logger.Collector.GetSnapshot().Where(record => record.Level == LogLevel.Warning).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains(warnings, record => record.Message.Contains("period 99"));
        Assert.Contains(warnings, record => record.Message.Contains("2015-04-02"));
    }

    [Fact]
    [Trait("Requirement", "10")]
    public async Task Scheduler_LogsFailureWithExceptionAndSuccessfulRetryWithinExtractScope()
    {
        var logger = new FakeLogger<ExtractScheduler>();
        var exception = new InvalidOperationException("temporary failure");
        var scheduler = CreateScheduler(logger, call => call == 1
            ? Task.FromException<string>(exception)
            : Task.FromResult(@"C:\Reports\PowerPosition_20150401_1300.csv"));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            var failure = await WaitForLogAsync(logger, record => record.Level == LogLevel.Error && record.Exception is not null);
            Assert.Same(exception, failure.Exception);
            Assert.Contains("attempt 1", failure.Message);
            Assert.Contains(failure.Scopes, scope =>
                scope is IEnumerable<KeyValuePair<string, object>> values &&
                values.Any(pair => pair.Key == "ExtractTime"));

            await Task.Delay(20);
            _time.Advance(TimeSpan.FromSeconds(10));
            var success = await WaitForLogAsync(logger, record =>
                record.Message.Contains("completed on attempt 2") &&
                record.Message.Contains(@"C:\Reports\PowerPosition_20150401_1300.csv"));
            Assert.Contains(@"C:\Reports\PowerPosition_20150401_1300.csv", success.Message);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(Timeout);
            scheduler.Dispose();
        }
    }

    [Fact]
    [Trait("Requirement", "10")]
    public async Task Scheduler_LogsAbandonedExtractOnShutdown()
    {
        var logger = new FakeLogger<ExtractScheduler>();
        var scheduler = CreateScheduler(logger, _ => Task.FromException<string>(new InvalidOperationException("failed")));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await WaitForLogAsync(logger, record => record.Level == LogLevel.Error && record.Exception is not null);
            await Task.Delay(20);
            await scheduler.StopAsync(CancellationToken.None).WaitAsync(Timeout);

            Assert.Contains(
                logger.Collector.GetSnapshot(),
                record => record.Level == LogLevel.Error && record.Message.Contains("abandoned after 1 failed attempt"));
        }
        finally
        {
            scheduler.Dispose();
        }
    }

    private ExtractScheduler CreateScheduler(FakeLogger<ExtractScheduler> logger, Func<int, Task<string>> behaviour) =>
        new(
            new DelegateExtractor(behaviour),
            _time,
            Options.Create(new PowerPositionOptions
            {
                OutputDirectory = ".",
                IntervalMinutes = 5,
                RetryDelaySeconds = 10
            }),
            logger);

    private static async Task<FakeLogRecord> WaitForLogAsync(
        FakeLogger logger,
        Func<FakeLogRecord, bool> predicate)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < Timeout)
        {
            var record = logger.Collector.GetSnapshot().FirstOrDefault(predicate);
            if (record is not null)
            {
                return record;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("The expected log record was not emitted.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class DelegateExtractor(Func<int, Task<string>> behaviour) : IPowerPositionExtractor
    {
        private int _callCount;

        public Task<string> ExtractAsync(DateTimeOffset extractTime, CancellationToken cancellationToken) =>
            behaviour(Interlocked.Increment(ref _callCount));
    }
}
