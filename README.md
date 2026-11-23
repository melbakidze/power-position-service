# PowerPosition Service

The PowerPosition Windows Worker Service retrieves the next local calendar day's day-ahead trades, aggregates them into London wall-clock hourly periods, and atomically writes a CSV report on a fixed cadence.

## Solution structure

```text
PowerPosition.sln                                      Solution containing service and tests
nuget.config                                           Restricts package restore to nuget.org
lib/PowerService.dll                                   Supplied Petroineos trade service
src/PowerPosition.Service/PowerPosition.Service.csproj Worker Service project and package references
src/PowerPosition.Service/Program.cs                   Entry point and bootstrap logging
src/PowerPosition.Service/PowerPositionHost.cs         Host composition
src/PowerPosition.Service/appsettings.json             Default application and Serilog configuration
src/PowerPosition.Service/Properties/launchSettings.json Development launch profile
src/PowerPosition.Service/Configuration/PowerPositionOptions.cs Validated startup options
src/PowerPosition.Service/Domain/TradingCalendar.cs    Trading dates and DST-aware period boundaries
src/PowerPosition.Service/Domain/HourlyPosition.cs     Aggregated period record
src/PowerPosition.Service/Domain/PositionAggregator.cs  Trade validation and volume aggregation
src/PowerPosition.Service/Output/PowerPositionCsvWriter.cs CSV formatting and atomic output
src/PowerPosition.Service/Extraction/IPowerPositionExtractor.cs Extractor contract
src/PowerPosition.Service/Extraction/PowerPositionExtractor.cs Trade retrieval and report generation
src/PowerPosition.Service/Scheduling/ExtractScheduler.cs Fixed-cadence scheduling and retry lifecycle
tests/PowerPosition.Tests/PowerPosition.Tests.csproj    xUnit project and test dependencies
tests/PowerPosition.Tests/TestData.cs                   Shared trades and fake power service
tests/PowerPosition.Tests/TradingCalendarTests.cs       Trading date and DST coverage
tests/PowerPosition.Tests/PositionAggregatorTests.cs    Period-number aggregation coverage
tests/PowerPosition.Tests/PowerPositionCsvWriterTests.cs CSV formatting and file coverage
tests/PowerPosition.Tests/PowerPositionExtractorTests.cs End-to-end extraction coverage
tests/PowerPosition.Tests/ExtractSchedulerTests.cs      Timing, retry, and shutdown coverage
tests/PowerPosition.Tests/HostCompositionTests.cs       Host wiring and configuration coverage
tests/PowerPosition.Tests/LoggingTests.cs               Structured logging coverage
```

## Configuration

Settings are bound and validated once at startup. Editing `appsettings.json` does not change the active options or schedule; restart the service to apply changes. Command-line values override the JSON configuration, for example `--PowerPosition:IntervalMinutes=1`.

| Setting | Default | Description |
|---|---|---|
| PowerPosition:OutputDirectory | C:\PowerPosition\Reports | CSV destination; relative paths resolve from the install directory |
| PowerPosition:IntervalMinute | 5 | Extract cadence, from 1 through 1440 minutes |
| PowerPosition:RetryDelaySeconds | 10 | Retry delay, from 1 through 3600 seconds |
| PowerPosition:TimeZoneId | Europe/London | Time zone for trading dates and report timestamps |
| Serilog | Console and daily rolling file | Structured logging configuration; file logs retain 31 days |


## Notes

- A trading day runs from 23:00 on the preceding local date through 23:00 on the trading date. Boundaries are converted to UTC before period generation, producing 23 periods on the spring-forward day and 25 on the autumn-back day (including both local 01:00 periods).
- The requested trade date is always the next local calendar date, including between 23:00 and midnight.
- The scheduler starts an extract at service startup and then follows a monotonic fixed cadence. Downtime is not backfilled: extracts are point-in-time snapshots, and startup retrieves the current position after restart.
- System clock changes do not affect cadence. The scheduled extract time is used for the filename and is preserved during retries.
- Reports use UTF-8 without a BOM, CRLF line endings, invariant-culture values, and a temporary file followed by an atomic move.
- PowerService failures are retried after RetryDelaySeconds until success or shutdown. Concurrent slow extracts do not block later scheduled runs.
- Logs go to the console and logs\PowerPosition-YYYYMMDD.log beside the executable (31-day retention). Every extract run carries an `ExtractTime` logging scope.
