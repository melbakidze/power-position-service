using PowerPosition.Service.Domain;

namespace PowerPosition.Tests;

public sealed class TradingCalendarTests
{
    private readonly TradingCalendar _calendar = new(TestData.London);

    [Theory]
    [InlineData("2015-01-03T00:00:00Z", "2015-01-04")]
    [InlineData("2015-01-03T23:30:00Z", "2015-01-04")]
    [InlineData("2015-06-03T22:30:00Z", "2015-06-04")]
    [InlineData("2015-06-03T23:30:00Z", "2015-06-05")]
    [Trait("Requirement", "DayAhead")]
    public void GetTradingDate_IsNextLocalCalendarDay(string instant, string expected)
    {
        var result = _calendar.GetTradingDate(DateTimeOffset.Parse(instant));

        Assert.Equal(DateTime.Parse(expected), result);
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void GetPeriodStartTimes_NormalDay_Has24HoursStartingAt2300PreviousDay()
    {
        var starts = _calendar.GetPeriodStartTimes(new DateTime(2015, 4, 1));

        Assert.Equal(24, starts.Count);
        Assert.Equal(new DateTime(2015, 3, 31, 23, 0, 0), starts[0]);
        Assert.Equal(new DateTime(2015, 4, 1, 0, 0, 0), starts[1]);
        Assert.Equal(new DateTime(2015, 4, 1, 22, 0, 0), starts[23]);
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void GetPeriodStartTimes_ClocksGoForward_Has23HoursAndSkips0100()
    {
        var starts = _calendar.GetPeriodStartTimes(new DateTime(2026, 3, 29));

        Assert.Equal(23, starts.Count);
        Assert.Equal(["23:00", "00:00", "02:00", "03:00"], starts.Take(4).Select(time => time.ToString("HH:mm")));
        Assert.Equal("22:00", starts[^1].ToString("HH:mm"));
    }

    [Fact]
    [Trait("Requirement", "2")]
    public void GetPeriodStartTimes_ClocksGoBack_Has25HoursAndRepeats0100()
    {
        var starts = _calendar.GetPeriodStartTimes(new DateTime(2026, 10, 25));

        Assert.Equal(25, starts.Count);
        Assert.Equal(["23:00", "00:00", "01:00", "01:00", "02:00"], starts.Take(5).Select(time => time.ToString("HH:mm")));
        Assert.Equal("22:00", starts[^1].ToString("HH:mm"));
    }
}
