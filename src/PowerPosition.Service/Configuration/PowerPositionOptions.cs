using System.ComponentModel.DataAnnotations;

namespace PowerPosition.Service.Configuration;

/// <summary>Configuration for report output, scheduling, and the trading time zone.</summary>
/// <remarks>Options are read once at service startup; changes require a restart.</remarks>
public sealed class PowerPositionOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "PowerPosition";

    /// <summary>Directory for generated CSV reports. Relative paths resolve from the install directory.</summary>
    [Required(AllowEmptyStrings = false)]
    public string OutputDirectory { get; set; } = "";

    /// <summary>Extract cadence in minutes.</summary>
    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 5;

    /// <summary>Delay between failed extraction attempts in seconds.</summary>
    [Range(1, 3600)]
    public int RetryDelaySeconds { get; set; } = 10;

    /// <summary>Time-zone identifier used for trading-day boundaries and report timestamps.</summary>
    [Required(AllowEmptyStrings = false)]
    public string TimeZoneId { get; set; } = "Europe/London";
}
