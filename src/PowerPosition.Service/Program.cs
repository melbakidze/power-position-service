using Serilog;
using PowerPosition.Service;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "startup-.log"), rollingInterval: RollingInterval.Day)
    .CreateBootstrapLogger();

try
{
    var builder = PowerPositionHost.CreateBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    var host = builder.Build();
    var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<PowerPosition.Service.Configuration.PowerPositionOptions>>().Value;
    Log.Information(
        "PowerPosition service starting. OutputDirectory={OutputDirectory}, IntervalMinutes={IntervalMinutes}, TimeZone={TimeZoneId}",
        Path.GetFullPath(options.OutputDirectory, AppContext.BaseDirectory),
        options.IntervalMinutes,
        options.TimeZoneId);
    await host.RunAsync();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "PowerPosition service terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
