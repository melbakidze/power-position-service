using Microsoft.Extensions.Logging;
using Services;

namespace PowerPosition.Service.Domain;

/// <summary>Aggregates trade volumes by one-based period number.</summary>
public sealed class PositionAggregator
{
    private readonly TradingCalendar _calendar;
    private readonly ILogger<PositionAggregator> _logger;

    /// <summary>Creates an aggregator using the specified trading calendar and logger.</summary>
    public PositionAggregator(TradingCalendar calendar, ILogger<PositionAggregator> logger)
    {
        _calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Combines all valid trades into one row for every period in the trading day.</summary>
    public IReadOnlyList<HourlyPosition> Aggregate(DateTime tradingDate, IEnumerable<PowerTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var periodStarts = _calendar.GetPeriodStartTimes(tradingDate);
        var volumes = new double[periodStarts.Count];
        var tradeCount = 0;

        foreach (var trade in trades)
        {
            tradeCount++;
            if (trade is null || trade.Periods is null)
            {
                _logger.LogWarning("Skipping trade #{TradeIndex}: trade or its periods are null", tradeCount);
                continue;
            }

            if (trade.Date.Date != tradingDate.Date)
            {
                _logger.LogWarning(
                    "Skipping trade #{TradeIndex}: trade date {TradeDate:yyyy-MM-dd} does not match trading date {TradingDate:yyyy-MM-dd}",
                    tradeCount,
                    trade.Date,
                    tradingDate);
                continue;
            }

            foreach (var period in trade.Periods)
            {
                if (period.Period < 1 || period.Period > volumes.Length)
                {
                    _logger.LogWarning(
                        "Skipping period {Period} of trade #{TradeIndex}: trading date {TradingDate:yyyy-MM-dd} has {PeriodCount} periods",
                        period.Period,
                        tradeCount,
                        tradingDate,
                        volumes.Length);
                    continue;
                }

                volumes[period.Period - 1] += period.Volume;
            }
        }

        _logger.LogDebug(
            "Aggregated {TradeCount} trades into {PeriodCount} periods for {TradingDate:yyyy-MM-dd}",
            tradeCount,
            periodStarts.Count,
            tradingDate);

        return Enumerable.Range(0, periodStarts.Count)
            .Select(index => new HourlyPosition(index + 1, periodStarts[index], volumes[index]))
            .ToArray();
    }
}
