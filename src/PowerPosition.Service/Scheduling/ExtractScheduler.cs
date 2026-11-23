using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Extraction;

namespace PowerPosition.Service.Scheduling;

/// <summary>
/// Runs extracts on a fixed monotonic cadence without waiting for previous runs. It catches up overdue
/// scheduled runs, retries failures, preserves each scheduled timestamp, and drains in-flight work on shutdown.
/// </summary>
public sealed class ExtractScheduler : BackgroundService
{
    private readonly IPowerPositionExtractor _extractor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExtractScheduler> _logger;
    private readonly TimeSpan _interval;
    private readonly TimeSpan _retryDelay;

    /// <summary>Creates a scheduler from extractor, monotonic clock, and validated startup options.</summary>
    public ExtractScheduler(
        IPowerPositionExtractor extractor,
        TimeProvider timeProvider,
        IOptions<PowerPositionOptions> options,
        ILogger<ExtractScheduler> logger)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
        _retryDelay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var running = new List<Task>();
        var startTimestamp = _timeProvider.GetTimestamp();

        for (long run = 0; !stoppingToken.IsCancellationRequested; run++)
        {
            var elapsed = _timeProvider.GetElapsedTime(startTimestamp);
            var lateness = elapsed - (_interval * run);
            var scheduledTime = _timeProvider.GetUtcNow() - (lateness > TimeSpan.Zero ? lateness : TimeSpan.Zero);
            running.RemoveAll(task => task.IsCompleted);
            running.Add(Task.Run(() => RunWithRetryAsync(scheduledTime, stoppingToken), CancellationToken.None));

            var untilNextRun = (_interval * (run + 1)) - _timeProvider.GetElapsedTime(startTimestamp);
            if (untilNextRun < TimeSpan.Zero)
            {
                _logger.LogWarning("Scheduler is {Lag} behind; running overdue extract now", TimeSpan.Zero - untilNextRun);
            }

            try
            {
                await DelayAsync(untilNextRun, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Stopping scheduler with {InFlightCount} in-flight extract(s)", running.Count);
        await Task.WhenAll(running);
        _logger.LogInformation("Scheduler stopped");
    }

    internal async Task RunWithRetryAsync(DateTimeOffset scheduledTime, CancellationToken stoppingToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["ExtractTime"] = scheduledTime.ToString("O")
        });

        for (var attempt = 1; ; attempt++)
        {
            var attemptStarted = _timeProvider.GetTimestamp();
            _logger.LogInformation("Starting extract attempt {Attempt} for {ExtractTime}", attempt, scheduledTime);
            try
            {
                var path = await _extractor.ExtractAsync(scheduledTime, stoppingToken);
                _logger.LogInformation("Extract completed on attempt {Attempt}: {Path}", attempt, path);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Extract cancelled by service shutdown on attempt {Attempt}", attempt);
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Extract failed on attempt {Attempt}; retrying in {RetryDelay}",
                    attempt,
                    _retryDelay);

                try
                {
                    await DelayAsync(_retryDelay - _timeProvider.GetElapsedTime(attemptStarted), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(
                        "Extract abandoned after {Attempt} failed attempt(s) due to service shutdown",
                        attempt);
                    return;
                }
            }
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero
            ? Task.Delay(delay, _timeProvider, cancellationToken)
            : Task.CompletedTask;
}
