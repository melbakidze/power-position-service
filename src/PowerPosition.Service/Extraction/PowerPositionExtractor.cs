using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Domain;
using PowerPosition.Service.Output;
using Services;

namespace PowerPosition.Service.Extraction;

/// <summary>Retrieves, aggregates, and writes the day-ahead power position.</summary>
public sealed class PowerPositionExtractor : IPowerPositionExtractor
{
    private readonly IPowerService _powerService;
    private readonly TradingCalendar _calendar;
    private readonly PositionAggregator _aggregator;
    private readonly PowerPositionCsvWriter _writer;
    private readonly string _outputDirectory;
    private readonly ILogger<PowerPositionExtractor> _logger;

    /// <summary>Creates an extractor with its dependencies and startup configuration.</summary>
    public PowerPositionExtractor(
        IPowerService powerService,
        TradingCalendar calendar,
        PositionAggregator aggregator,
        PowerPositionCsvWriter writer,
        IOptions<PowerPositionOptions> options,
        ILogger<PowerPositionExtractor> logger)
    {
        _powerService = powerService ?? throw new ArgumentNullException(nameof(powerService));
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _aggregator = aggregator ?? throw new ArgumentNullException(nameof(aggregator));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        ArgumentNullException.ThrowIfNull(options);
        _outputDirectory = Path.GetFullPath(options.Value.OutputDirectory, AppContext.BaseDirectory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> ExtractAsync(DateTimeOffset extractTime, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var localExtractTime = _calendar.ToLocalTime(extractTime);
        var tradingDate = _calendar.GetTradingDate(extractTime);
        _logger.LogInformation(
            "Retrieving trades for trading date {TradingDate:yyyy-MM-dd} (extract time {LocalExtractTime:yyyy-MM-dd HH:mm} local)",
            tradingDate,
            localExtractTime);

        var trades = (await _powerService.GetTradesAsync(tradingDate).WaitAsync(cancellationToken)).ToList();
        _logger.LogInformation("Retrieved {TradeCount} trades for {TradingDate:yyyy-MM-dd}", trades.Count, tradingDate);
        var positions = _aggregator.Aggregate(tradingDate, trades);
        var path = Path.Combine(_outputDirectory, PowerPositionCsvWriter.GetFileName(localExtractTime));
        await _writer.WriteAsync(path, positions, cancellationToken);
        _logger.LogInformation(
            "Wrote {PeriodCount} periods to {Path} in {ElapsedMs} ms",
            positions.Count,
            path,
            stopwatch.ElapsedMilliseconds);
        return path;
    }
}
