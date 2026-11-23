namespace PowerPosition.Service.Domain;

/// <summary>An aggregate volume for one numbered period and its local wall-clock start.</summary>
/// <param name="Period">One-based period number within the trading day.</param>
/// <param name="LocalStartTime">Local wall-clock time at the beginning of the period.</param>
/// <param name="Volume">Total volume of all matching trades.</param>
public sealed record HourlyPosition(int Period, DateTime LocalStartTime, double Volume);
