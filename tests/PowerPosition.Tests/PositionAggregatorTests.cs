using Microsoft.Extensions.Logging.Abstractions;
using PowerPosition.Service.Domain;

namespace PowerPosition.Tests;

public sealed class PositionAggregatorTests
{
    private static readonly DateTime Date = new(2015, 4, 1);
    private readonly PositionAggregator _aggregator = new(new TradingCalendar(TestData.London), NullLogger<PositionAggregator>.Instance);

    [Fact]
    [Trait("Requirement", "2")]
    public void Aggregate_SpecExample_SumsVolumesPerHour()
    {
        var result = _aggregator.Aggregate(Date, TestData.SpecExampleTrades(Date));

        Assert.Equal(24, result.Count);
        Assert.Equal(Enumerable.Range(1, 24), result.Select(item => item.Period));
        Assert.All(result.Take(11), item => Assert.Equal(150, item.Volume));
        Assert.All(result.Skip(11), item => Assert.Equal(80, item.Volume));
        Assert.Equal(new DateTime(2015, 3, 31, 23, 0, 0), result[0].LocalStartTime);
        Assert.Equal(new DateTime(2015, 4, 1, 10, 0, 0), result[11].LocalStartTime);
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void Aggregate_NoTrades_ReturnsZeroForEveryHour()
    {
        var result = _aggregator.Aggregate(Date, []);

        Assert.Equal(24, result.Count);
        Assert.All(result, item => Assert.Equal(0, item.Volume));
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void Aggregate_PeriodsAreMatchedByPeriodNumberNotArrayOrder()
    {
        var trade = TestData.Trade(Date, TestData.Repeat(0, 24));
        trade.Periods[0].Period = 2;
        trade.Periods[0].Volume = 7;
        trade.Periods[1].Period = 1;
        trade.Periods[1].Volume = 3;

        var result = _aggregator.Aggregate(Date, [trade]);

        Assert.Equal(3, result[0].Volume);
        Assert.Equal(7, result[1].Volume);
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void Aggregate_IgnoresOutOfRangePeriodsAndTradesForOtherDates()
    {
        var shortDay = new DateTime(2026, 3, 29);
        var result = _aggregator.Aggregate(
            shortDay,
            [
                TestData.Trade(shortDay, TestData.Repeat(1, 24)),
                TestData.Trade(shortDay.AddDays(1), TestData.Repeat(1000, 23))
            ]);

        Assert.Equal(23, result.Count);
        Assert.All(result, item => Assert.Equal(1, item.Volume));
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void Aggregate_LongDay_Returns25Hours()
    {
        var longDay = new DateTime(2026, 10, 25);
        var result = _aggregator.Aggregate(
            longDay,
            [
                TestData.Trade(longDay, TestData.Repeat(10, 25)),
                TestData.Trade(longDay, TestData.Repeat(5, 25))
            ]);

        Assert.Equal(25, result.Count);
        Assert.All(result, item => Assert.Equal(15, item.Volume));
    }
}
