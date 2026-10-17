using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Geocoding;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Fitzobit.Infrastructure.Diagnostics;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Geocoding;
using Fitzobit.Infrastructure.Identity;
using Fitzobit.Infrastructure.Messaging;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fitzobit.Infrastructure;

public static class DependencyInjection
{
    /// <remarks>The host registers <see cref="ICurrentUser"/> (the signed-in user).</remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<ExportOptions>(configuration.GetSection(ExportOptions.SectionName));
        services.Configure<LocationClassificationOptions>(configuration.GetSection(LocationClassificationOptions.SectionName));
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddDbContext<FitzobitDbContext>(builder => DatabaseConfigurator.Configure(builder, databaseOptions.ConnectionString));
        services.AddScoped<IFitzobitDbContext>(sp => sp.GetRequiredService<FitzobitDbContext>());
        services.AddScoped<ICustomerIdGenerator, SequenceCustomerIdGenerator>();

        services.AddSingleton<IPasswordHashing, IdentityPasswordHashing>();
        services.AddSingleton<ICustomerExportWriter, CustomerExcelWriter>();
        services.AddSingleton<ICustomerExportWriter, CustomerPdfWriter>();
        services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>(c => c.Timeout = TimeSpan.FromSeconds(20));
        AddGeocoding(services, configuration);
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<SystemInfoService>();
        return services;
    }

    /// <summary>Geocoding:Provider selects the implementation: Nominatim (default), Google or None.</summary>
    private static void AddGeocoding(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GeocodingOptions>(configuration.GetSection(GeocodingOptions.SectionName));
        var options = configuration.GetSection(GeocodingOptions.SectionName).Get<GeocodingOptions>() ?? new GeocodingOptions();
        switch (options.Provider.Trim().ToLowerInvariant())
        {
            case "nominatim":
                services.AddSingleton<NominatimRequestGate>();
                services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(c =>
                {
                    c.BaseAddress = new Uri(options.Nominatim.BaseUrl.TrimEnd('/') + "/");
                    c.Timeout = TimeSpan.FromSeconds(options.Nominatim.TimeoutSeconds);
                    c.DefaultRequestHeaders.UserAgent.ParseAdd(options.Nominatim.UserAgent);
                });
                break;
            case "google":
                services.AddSingleton<GoogleRequestGate>();
                services.AddHttpClient<IGeocodingService, GoogleGeocodingService>(c =>
                {
                    c.BaseAddress = new Uri(options.Google.BaseUrl);
                    c.Timeout = TimeSpan.FromSeconds(options.Google.TimeoutSeconds);
                });
                break;
            default:
                services.AddSingleton<IGeocodingService, DisabledGeocodingService>();
                break;
        }
    }
}
