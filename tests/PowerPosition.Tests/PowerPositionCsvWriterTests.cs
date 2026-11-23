using System.Globalization;
using PowerPosition.Service.Domain;
using PowerPosition.Service.Output;

namespace PowerPosition.Tests;

public sealed class PowerPositionCsvWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PowerPositionTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("2014-12-20T18:37:59", "PowerPosition_20141220_1837.csv")]
    [InlineData("2015-01-05T09:05:00", "PowerPosition_20150105_0905.csv")]
    [Trait("Requirement", "4")]
    public void GetFileName_UsesYyyyMmDdHhMmFormat(string time, string expected)
    {
        Assert.Equal(expected, PowerPositionCsvWriter.GetFileName(DateTime.Parse(time, CultureInfo.InvariantCulture)));
    }

    [Fact]
    [Trait("Requirement", "3")]
    public void Format_WritesHeaderAnd24HourTimesWithInvariantNumbers()
    {
        var positions = new[]
        {
            new HourlyPosition(1, new DateTime(2015, 3, 31, 23, 0, 0), 150),
            new HourlyPosition(2, new DateTime(2015, 4, 1, 13, 0, 0), -20.5)
        };

        Assert.Equal("Local Time,Volume\r\n23:00,150\r\n13:00,-20.5\r\n", PowerPositionCsvWriter.Format(positions));
    }

    [Fact]
    [Trait("Requirement", "3")]
    [Trait("Requirement", "5")]
    public async Task WriteAsync_CreatesDirectoryAndLeavesNoTempFile()
    {
        var folder = Path.Combine(_directory, "nested");
        var path = Path.Combine(folder, "report.csv");
        var writer = new PowerPositionCsvWriter();

        await writer.WriteAsync(path, [new HourlyPosition(1, new DateTime(2015, 3, 31, 23, 0, 0), 1)], CancellationToken.None);

        Assert.Equal("Local Time,Volume\r\n23:00,1\r\n", await File.ReadAllTextAsync(path));
        Assert.Equal(["report.csv"], Directory.GetFiles(folder).Select(Path.GetFileName));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
