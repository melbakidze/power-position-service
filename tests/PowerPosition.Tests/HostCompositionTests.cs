using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PowerPosition.Service;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Extraction;
using PowerPosition.Service.Scheduling;

namespace PowerPosition.Tests;

public sealed class HostCompositionTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerPositionTests", Guid.NewGuid().ToString("N"));
    private readonly string _output;

    public HostCompositionTests()
    {
        Directory.CreateDirectory(_root);
        _output = Path.Combine(_root, "reports");
    }

    [Fact]
    [Trait("Requirement", "1")]
    public void Host_RegistersScheduler_AndUsesConsoleLifetimeWhenNotRunAsService()
    {
        WriteSettings(5, _output);
        using var host = BuildHost();

        Assert.Contains(host.Services.GetServices<IHostedService>(), service => service is ExtractScheduler);
        Assert.False(WindowsServiceHelpers.IsWindowsService());
        Assert.IsType<ConsoleLifetime>(host.Services.GetRequiredService<IHostLifetime>());
    }

    [Fact]
    [Trait("Requirement", "5")]
    [Trait("Requirement", "6")]
    [Trait("Requirement", "8")]
    public async Task Host_WritesExtractsToConfiguredDirectoryAtConfiguredInterval()
    {
        WriteSettings(7, _output);
        var time = new FakeTimeProvider(new DateTimeOffset(2015, 4, 1, 17, 37, 0, TimeSpan.Zero));
        using var host = BuildHost(services =>
        {
            services.AddSingleton<TimeProvider>(time);
            services.AddSingleton<Services.IPowerService>(
                new TestData.FakePowerService(date => TestData.SpecExampleTrades(date)));
        });

        await host.StartAsync();
        await WaitUntilAsync(() => File.Exists(Path.Combine(_output, "PowerPosition_20150401_1837.csv")));
        Assert.Equal(TestData.SpecExampleCsv, await File.ReadAllTextAsync(Path.Combine(_output, "PowerPosition_20150401_1837.csv")));
        time.Advance(TimeSpan.FromMinutes(6));
        await Task.Delay(200);
        Assert.Single(Directory.GetFiles(_output));
        time.Advance(TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => File.Exists(Path.Combine(_output, "PowerPosition_20150401_1844.csv")));
        await host.StopAsync().WaitAsync(Timeout);
    }

    [Fact]
    [Trait("Requirement", "5")]
    [Trait("Requirement", "6")]
    public async Task Host_InvalidConfiguration_FailsAtStartUp()
    {
        foreach (var (interval, output) in new[] { (0, "reports"), (5, "") })
        {
            WriteSettings(interval, output);
            using var host = BuildHost();
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        }
    }

    [Fact]
    [Trait("Requirement", "9")]
    public async Task Host_ConfigurationChangedAfterStartUp_IsNotApplied()
    {
        WriteSettings(7, _output);
        using var host = BuildHost();
        var startupOptions = host.Services.GetRequiredService<IOptions<PowerPositionOptions>>();
        Assert.Equal(7, startupOptions.Value.IntervalMinutes);

        WriteSettings(9, _output);
        await WaitUntilAsync(() =>
            host.Services.GetRequiredService<IConfiguration>()["PowerPosition:IntervalMinutes"] == "9");

        Assert.Equal(7, startupOptions.Value.IntervalMinutes);
        Assert.Equal(7, host.Services.GetRequiredService<IOptions<PowerPositionOptions>>().Value.IntervalMinutes);
    }

    private IHost BuildHost(Action<IServiceCollection>? overrides = null)
    {
        var builder = PowerPositionHost.CreateBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = _root,
            EnvironmentName = Environments.Production
        });
        overrides?.Invoke(builder.Services);
        return builder.Build();
    }

    private void WriteSettings(int intervalMinutes, string outputDirectory)
    {
        var settings = JsonSerializer.Serialize(new
        {
            PowerPosition = new
            {
                OutputDirectory = outputDirectory,
                IntervalMinutes = intervalMinutes,
                RetryDelaySeconds = 10,
                TimeZoneId = "Europe/London"
            }
        });
        var temporaryPath = Path.Combine(_root, "appsettings.json.tmp");
        File.WriteAllText(temporaryPath, settings);
        File.Move(temporaryPath, Path.Combine(_root, "appsettings.json"), overwrite: true);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > Timeout)
            {
                throw new TimeoutException("The expected host condition was not reached.");
            }

            await Task.Delay(20);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
