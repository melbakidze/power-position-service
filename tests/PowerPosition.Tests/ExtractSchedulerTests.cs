using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Extraction;
using PowerPosition.Service.Scheduling;

namespace PowerPosition.Tests;

public sealed class ExtractSchedulerTests : IAsyncDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2015, 4, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly AdjustableClock _clock;
    private readonly DateTimeOffset _start;
    private ExtractScheduler? _scheduler;

    public ExtractSchedulerTests()
    {
        _clock = new AdjustableClock(_time);
        _start = _clock.GetUtcNow();
    }

    [Fact]
    [Trait("Requirement", "6")]
    [Trait("Requirement", "8")]
    public async Task RunsImmediatelyOnStartAndThenEveryInterval()
    {
        var extractor = new RecordingExtractor();
        await StartAsync(extractor);

        Assert.Equal(_start, await extractor.NextCallAsync());
        _time.Advance(Interval - TimeSpan.FromSeconds(1));
        await extractor.AssertNoCallAsync();
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(_start + Interval, await extractor.NextCallAsync());
        _time.Advance(Interval);
        Assert.Equal(_start + (Interval * 2), await extractor.NextCallAsync());
    }

    [Fact]
    [Trait("Requirement", "7")]
    public async Task RetriesFailedExtractWithOriginalExtractTime()
    {
        var extractor = new RecordingExtractor((call, _) =>
            call <= 2 ? Task.FromException<string>(new InvalidOperationException("temporary")) : Task.FromResult("ok"));
        await StartAsync(extractor);

        Assert.Equal(_start, await extractor.NextCallAsync());
        await Task.Delay(20);
        _time.Advance(RetryDelay);
        Assert.Equal(_start, await extractor.NextCallAsync());
        await Task.Delay(20);
        _time.Advance(RetryDelay);
        Assert.Equal(_start, await extractor.NextCallAsync());
        _time.Advance(RetryDelay);
        await extractor.AssertNoCallAsync();
    }

    [Fact]
    [Trait("Requirement", "6")]
    [Trait("Requirement", "7")]
    public async Task SlowExtractDoesNotDelayNextScheduledExtract()
    {
        var extractor = new RecordingExtractor((call, token) =>
            call == 1 ? Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token).ContinueWith(_ => "ok", token) : Task.FromResult("ok"));
        await StartAsync(extractor);

        Assert.Equal(_start, await extractor.NextCallAsync());
        _time.Advance(Interval);
        Assert.Equal(_start + Interval, await extractor.NextCallAsync());
    }

    [Fact]
    [Trait("Requirement", "1")]
    public async Task StopAsync_CancelsInFlightExtractAndCompletes()
    {
        var extractor = new RecordingExtractor((_, token) =>
            Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, token).ContinueWith(_ => "ok", token));
        await StartAsync(extractor);
        await extractor.NextCallAsync();

        await _scheduler!.StopAsync(CancellationToken.None).WaitAsync(Timeout);

        Assert.True(_scheduler.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Requirement", "6")]
    [Trait("Requirement", "7")]
    public async Task SystemClockSetBack_DoesNotPauseScheduleAndStampsCurrentTime()
    {
        var extractor = new RecordingExtractor();
        await StartAsync(extractor);
        Assert.Equal(_start, await extractor.NextCallAsync());

        _clock.WallClockOffset = TimeSpan.FromHours(-1);
        _time.Advance(Interval);
        Assert.Equal(_start + Interval - TimeSpan.FromHours(1), await extractor.NextCallAsync());
        _time.Advance(Interval);
        Assert.Equal(_start + (Interval * 2) - TimeSpan.FromHours(1), await extractor.NextCallAsync());
    }

    [Fact]
    [Trait("Requirement", "6")]
    [Trait("Requirement", "7")]
    public async Task SystemClockSetForward_DoesNotTriggerBurstOfExtracts()
    {
        var extractor = new RecordingExtractor();
        await StartAsync(extractor);
        Assert.Equal(_start, await extractor.NextCallAsync());

        _clock.WallClockOffset = TimeSpan.FromHours(1);
        _time.Advance(Interval);
        Assert.Equal(_start + Interval + TimeSpan.FromHours(1), await extractor.NextCallAsync());
        await extractor.AssertNoCallAsync();
    }

    private async Task StartAsync(RecordingExtractor extractor)
    {
        _scheduler = new ExtractScheduler(
            extractor,
            _clock,
            Options.Create(new PowerPositionOptions
            {
                OutputDirectory = ".",
                IntervalMinutes = 5,
                RetryDelaySeconds = 10
            }),
            NullLogger<ExtractScheduler>.Instance);
        await _scheduler.StartAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_scheduler is not null)
        {
            await _scheduler.StopAsync(CancellationToken.None).WaitAsync(Timeout);
            _scheduler.Dispose();
        }
    }

    private sealed class AdjustableClock(FakeTimeProvider inner) : TimeProvider
    {
        public TimeSpan WallClockOffset { get; set; }

        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + WallClockOffset;

        public override long GetTimestamp() => inner.GetTimestamp();

        public override long TimestampFrequency => inner.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            inner.CreateTimer(callback, state, dueTime, period);
    }

    private sealed class RecordingExtractor(Func<int, CancellationToken, Task<string>>? behaviour = null) : IPowerPositionExtractor
    {
        private readonly Channel<DateTimeOffset> _calls = Channel.CreateUnbounded<DateTimeOffset>();
        private int _callCount;

        public async Task<string> ExtractAsync(DateTimeOffset extractTime, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _callCount);
            await _calls.Writer.WriteAsync(extractTime, cancellationToken);
            return await (behaviour?.Invoke(call, cancellationToken) ?? Task.FromResult("ok"));
        }

        public Task<DateTimeOffset> NextCallAsync() => _calls.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public async Task AssertNoCallAsync()
        {
            Assert.False(_calls.Reader.TryRead(out _));
            await Task.Delay(200);
            Assert.False(_calls.Reader.TryRead(out _));
        }
    }
}
