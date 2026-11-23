using Services;

namespace PowerPosition.Tests;

internal static class TestData
{
    public static TimeZoneInfo London { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static PowerTrade Trade(DateTime date, params double[] volumes)
    {
        var trade = PowerTrade.Create(date, volumes.Length);
        for (var index = 0; index < volumes.Length; index++)
        {
            trade.Periods[index].Period = index + 1;
            trade.Periods[index].Volume = volumes[index];
        }

        return trade;
    }

    public static double[] Repeat(double value, int count) => Enumerable.Repeat(value, count).ToArray();

    public static PowerTrade[] SpecExampleTrades(DateTime date) =>
    [
        Trade(date, Repeat(100, 24)),
        Trade(date, [.. Repeat(50, 11), .. Repeat(-20, 13)])
    ];

    public static string SpecExampleCsv
    {
        get
        {
            var rows = new List<string> { "Local Time,Volume" };
            for (var hour = 23; hour < 24; hour++)
            {
                rows.Add($"{hour:00}:00,150");
            }

            for (var hour = 0; hour <= 9; hour++)
            {
                rows.Add($"{hour:00}:00,150");
            }

            for (var hour = 10; hour <= 22; hour++)
            {
                rows.Add($"{hour:00}:00,80");
            }

            return string.Join("\r\n", rows) + "\r\n";
        }
    }

    internal sealed class FakePowerService(Func<DateTime, IEnumerable<PowerTrade>> getTrades) : IPowerService
    {
        public List<DateTime> RequestedDates { get; } = [];

        public IEnumerable<PowerTrade> GetTrades(DateTime date)
        {
            RequestedDates.Add(date);
            return getTrades(date);
        }

        public Task<IEnumerable<PowerTrade>> GetTradesAsync(DateTime date) =>
            Task.FromResult(GetTrades(date));
    }
}
