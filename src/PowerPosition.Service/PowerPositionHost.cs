using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PowerPosition.Service.Configuration;
using PowerPosition.Service.Domain;
using PowerPosition.Service.Extraction;
using PowerPosition.Service.Output;
using PowerPosition.Service.Scheduling;
using Serilog;
using Services;

namespace PowerPosition.Service;

/// <summary>Configures Windows-service integration, logging, and application dependencies.</summary>
public static class PowerPositionHost
{
    /// <summary>The Windows Service Control Manager service name.</summary>
    public const string ServiceName = "PowerPosition";

    /// <summary>Creates a host builder with the production service and logging composition.</summary>
    public static HostApplicationBuilder CreateBuilder(HostApplicationBuilderSettings settings)
    {
        var builder = Host.CreateApplicationBuilder(settings);
        builder.Services.AddWindowsService(options => options.ServiceName = ServiceName);
        builder.Services.AddSerilog((services, configuration) =>
            configuration.ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext());
        builder.Services.AddPowerPosition(builder.Configuration);
        return builder;
    }

    /// <summary>Registers report generation, startup-validated configuration, and scheduling services.</summary>
    public static IServiceCollection AddPowerPosition(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PowerPositionOptions>()
            .Bind(configuration.GetSection(PowerPositionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PowerPositionOptions>>().Value;
            return new TradingCalendar(TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId));
        });
        services.AddSingleton<IPowerService, PowerService>();
        services.AddSingleton<PositionAggregator>();
        services.AddSingleton<PowerPositionCsvWriter>();
        services.AddSingleton<IPowerPositionExtractor, PowerPositionExtractor>();
        services.AddHostedService<ExtractScheduler>();
        return services;
    }
}
