using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Catalogue;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Imports;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Fitzobit.Infrastructure.Catalogue;
using Fitzobit.Infrastructure.Diagnostics;
using Fitzobit.Infrastructure.Exports;
using Fitzobit.Infrastructure.Identity;
using Fitzobit.Infrastructure.Imports;
using Fitzobit.Infrastructure.Messaging;
using Fitzobit.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fitzobit.Infrastructure;

public static class DependencyInjection
{
    /// <param name="basePath">Directory that relative database/catalogue paths are resolved against (the repository root).</param>
    /// <remarks>The host registers <see cref="ICurrentUser"/> (the signed-in user).</remarks>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string basePath)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<CatalogueSeedOptions>(configuration.GetSection(CatalogueSeedOptions.SectionName));
        services.PostConfigure<CatalogueSeedOptions>(o => o.BasePath ??= basePath);
        services.Configure<ImportOptions>(configuration.GetSection(ImportOptions.SectionName));
        services.Configure<ExportOptions>(configuration.GetSection(ExportOptions.SectionName));
        services.Configure<LocationClassificationOptions>(configuration.GetSection(LocationClassificationOptions.SectionName));
        services.Configure<WhatsAppOptions>(configuration.GetSection(WhatsAppOptions.SectionName));

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddDbContext<FitzobitDbContext>(builder => DatabaseProviderConfigurator.Configure(builder, databaseOptions, basePath));
        services.AddScoped<IFitzobitDbContext>(sp => sp.GetRequiredService<FitzobitDbContext>());

        services.AddSingleton<IPasswordHashing, IdentityPasswordHashing>();
        services.AddScoped<ICatalogueSource, ExcelCatalogueSource>();
        services.AddSingleton<ICustomerFileReader, ExcelCustomerFileReader>();
        services.AddSingleton<ICustomerTemplateGenerator, ExcelCustomerTemplateGenerator>();
        services.AddSingleton<ICustomerExcelWriter, CustomerExcelWriter>();
        services.AddHttpClient<IWhatsAppService, WhatsAppCloudApiService>(c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddScoped<DatabaseInitializer>();
        services.AddScoped<SystemInfoService>();
        return services;
    }
}
