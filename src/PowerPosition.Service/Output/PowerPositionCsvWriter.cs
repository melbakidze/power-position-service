using System.Globalization;
using System.Text;
using PowerPosition.Service.Domain;

namespace PowerPosition.Service.Output;

/// <summary>Formats and atomically writes PowerPosition CSV reports.</summary>
public sealed class PowerPositionCsvWriter
{
    /// <summary>The exact two-column report header.</summary>
    public const string Header = "Local Time,Volume";

    /// <summary>Formats the report name using the local extract timestamp.</summary>
    public static string GetFileName(DateTime localExtractTime) =>
        $"PowerPosition_{localExtractTime.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture)}.csv";

    /// <summary>Formats rows with invariant values and Windows CRLF line endings.</summary>
    public static string Format(IEnumerable<HourlyPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        var builder = new StringBuilder().Append(Header).Append("\r\n");
        foreach (var position in positions)
        {
            builder.Append(position.LocalStartTime.ToString("HH:mm", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(position.Volume.ToString(CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>Writes a complete UTF-8 CSV using a temporary file followed by an atomic replacement.</summary>
    public async Task WriteAsync(string path, IEnumerable<HourlyPosition> positions, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(positions);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, Format(positions), new UTF8Encoding(false), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }
}
