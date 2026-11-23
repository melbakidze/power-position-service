namespace PowerPosition.Service.Domain;

/// <summary>Maps day-ahead trading periods onto a local time zone, including daylight-saving transitions.</summary>
public sealed class TradingCalendar
{
    /// <summary>The trading day begins at 23:00 on the previous local calendar day.</summary>
    public const int DayStartHour = 23;

    /// <summary>Creates a calendar for the supplied time zone.</summary>
    public TradingCalendar(TimeZoneInfo timeZone)
    {
        TimeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    /// <summary>The time zone governing trading-day boundaries.</summary>
    public TimeZoneInfo TimeZone { get; }

    /// <summary>Converts an instant to local wall-clock time in the configured time zone.</summary>
    public DateTime ToLocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime;

    /// <summary>Returns the next local calendar date as the requested day-ahead trading date.</summary>
    public DateTime GetTradingDate(DateTimeOffset instant) => ToLocalTime(instant).Date.AddDays(1);

    /// <summary>Returns each period's local start time for a trading date.</summary>
    /// <remarks>UTC arithmetic preserves 23- and 25-hour trading days across daylight-saving changes.</remarks>
    public IReadOnlyList<DateTime> GetPeriodStartTimes(DateTime tradingDate)
    {
        var startUtc = LocalToUtc(tradingDate.Date.AddDays(-1).AddHours(DayStartHour));
        var endUtc = LocalToUtc(tradingDate.Date.AddHours(DayStartHour));
        var periodCount = (int)(endUtc - startUtc).TotalHours;
        var starts = new DateTime[periodCount];

        for (var index = 0; index < periodCount; index++)
        {
            starts[index] = TimeZoneInfo.ConvertTimeFromUtc(startUtc.AddHours(index), TimeZone);
        }

        return starts;
    }

    private DateTime LocalToUtc(DateTime localTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified), TimeZone);
}
