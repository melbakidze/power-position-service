# PowerPosition Service — Petroineos .NET Coding Challenge

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

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet build
dotnet test
dotnet test --filter Requirement=7
```

Every test is tagged with `[Trait("Requirement", "n")]`; the day-ahead date-argument cases use the `DayAhead` trait.

## Configuration

Settings are bound and validated once at startup. Editing `appsettings.json` does not change the active options or schedule; restart the service to apply changes. Command-line values override the JSON configuration, for example `--PowerPosition:IntervalMinutes=1`.

| Setting | Default | Description |
|---|---|---|
| `PowerPosition:OutputDirectory` | `C:\PowerPosition\Reports` | CSV destination; relative paths resolve from the install directory |
| `PowerPosition:IntervalMinutes` | `5` | Extract cadence, from 1 through 1440 minutes |
| `PowerPosition:RetryDelaySeconds` | `10` | Retry delay, from 1 through 3600 seconds |
| `PowerPosition:TimeZoneId` | `Europe/London` | Time zone for trading dates and report timestamps |
| `Serilog` | Console and daily rolling file | Structured logging configuration; file logs retain 31 days |

## Install as a Windows service

Publish and register the service from an elevated PowerShell prompt:

```powershell
dotnet publish src\PowerPosition.Service -c Release -o C:\Services\PowerPosition
sc.exe create PowerPosition binPath= "C:\Services\PowerPosition\PowerPosition.Service.exe" start= auto
sc.exe start PowerPosition
```

For console execution during development:

```powershell
dotnet run --project src\PowerPosition.Service
```

## Requirements traceability

| # | Requirement | Implementation | Tests |
|---|---|---|---|
| 1 | Windows service starts immediately, stops cleanly | `PowerPositionHost.AddWindowsService`, `ExtractScheduler` | `Host_RegistersScheduler_AndUsesConsoleLifetimeWhenNotRunAsService`, `StopAsync_CancelsInFlightExtractAndCompletes`; SCM execution should be verified manually on Windows |
| 2 | Aggregate positions by London local hour | `TradingCalendar`, `PositionAggregator` | `TradingCalendarTests.*`, `PositionAggregatorTests.*` |
| 3 | Two-column CSV, 24-hour time, invariant values | `PowerPositionCsvWriter.Format` | `Format_WritesHeaderAnd24HourTimesWithInvariantNumbers`, `WriteAsync_CreatesDirectoryAndLeavesNoTempFile` |
| 4 | Extract-time filename format | `PowerPositionCsvWriter.GetFileName` | `GetFileName_UsesYyyyMmDdHhMmFormat`, `PowerPositionExtractorTests.*` |
| 5 | Configurable output directory | `PowerPositionOptions.OutputDirectory` | `WriteAsync_CreatesDirectoryAndLeavesNoTempFile`, `Host_WritesExtractsToConfiguredDirectoryAtConfiguredInterval`, `Host_InvalidConfiguration_FailsAtStartUp` |
| 6 | Startup extract and configured recurring interval | `ExtractScheduler` and `IntervalMinutes` option | `RunsImmediatelyOnStartAndThenEveryInterval`, `SlowExtractDoesNotDelayNextScheduledExtract`, `Host_WritesExtractsToConfiguredDirectoryAtConfiguredInterval` |
| 7 | No missed scheduled extracts while running; non-blocking retries and monotonic fixed cadence | `ExtractScheduler` uses monotonic timestamps, concurrent runs, catch-up, and retry-until-success | `RetriesFailedExtractWithOriginalExtractTime`, `SlowExtractDoesNotDelayNextScheduledExtract`, `SystemClockSetBack_DoesNotPauseScheduleAndStampsCurrentTime`, `SystemClockSetForward_DoesNotTriggerBurstOfExtracts` |
| 8 | Extract cadence within tolerance | Fixed-cadence scheduling in `ExtractScheduler` | `RunsImmediatelyOnStartAndThenEveryInterval`, `Host_WritesExtractsToConfiguredDirectoryAtConfiguredInterval` |
| 9 | Configuration read at startup | `IOptions<PowerPositionOptions>`, `ValidateOnStart` | `Host_InvalidConfiguration_FailsAtStartUp`, `Host_ConfigurationChangedAfterStartUp_IsNotApplied` |
| 10 | Production-support logging | Serilog console and rolling file sinks, structured events, per-extract scope | `LoggingTests.*` |
| DayAhead | Request the next local calendar date, including after local midnight | `TradingCalendar.GetTradingDate` | `GetTradingDate_IsNextLocalCalendarDay`, `ExtractAsync_AfterLocalMidnight_RequestsFollowingDay` |

## Design notes

- A trading day runs from 23:00 on the preceding local date through 23:00 on the trading date. Boundaries are converted to UTC before period generation, producing 23 periods on the spring-forward day and 25 on the autumn-back day (including both local 01:00 periods).
- The requested trade date is always the next local calendar date, including between 23:00 and midnight.
- The scheduler starts an extract at service startup and then follows a monotonic fixed cadence. Downtime is not backfilled: extracts are point-in-time snapshots, and startup retrieves the current position after restart.
- System clock changes do not affect cadence. The scheduled extract time is used for the filename and is preserved during retries.
- Reports use UTF-8 without a BOM, CRLF line endings, invariant-culture values, and a temporary file followed by an atomic move.
- PowerService failures are retried after `RetryDelaySeconds` until success or shutdown. Concurrent slow extracts do not block later scheduled runs.
- Logs go to the console and `logs\PowerPosition-YYYYMMDD.log` beside the executable (31-day retention). Every extract run carries an `ExtractTime` logging scope.
