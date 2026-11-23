namespace PowerPosition.Service.Extraction;

/// <summary>Creates an aggregated CSV report for a scheduled extract time.</summary>
public interface IPowerPositionExtractor
{
    /// <summary>Extracts positions and returns the resulting CSV path.</summary>
    Task<string> ExtractAsync(DateTimeOffset extractTime, CancellationToken cancellationToken);
}
