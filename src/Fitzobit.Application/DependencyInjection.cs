using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Administration;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Catalogue;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Dashboard;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Imports;
using Fitzobit.Application.Imports.Locations;
using Fitzobit.Application.Imports.Plans;
using Fitzobit.Application.Imports.Validation;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fitzobit.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<CatalogueSeeder>();
        services.AddScoped<CatalogueService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DeliveryGroupAssignmentService>();
        services.AddScoped<DeliveryGroupQueryService>();
        services.AddScoped<DeliveryGroupService>();

        services.AddSingleton<ILocationClassificationService>(sp =>
            new LocationClassificationService(
                sp.GetService<Microsoft.Extensions.Options.IOptions<LocationClassificationOptions>>()?.Value ?? new LocationClassificationOptions()));
        services.AddScoped<CustomerRowValidator>();
        services.AddScoped<PlanCatalogue>();
        services.AddScoped<ImportUploadService>();
        services.AddScoped<ImportQueryService>();
        services.AddScoped<ImportWorkflowService>();
        services.AddScoped<ImportCommitService>();
        services.AddScoped<ImportBatchQueryService>();
        services.AddScoped<ImportTemplateService>();

        services.AddScoped<CustomerQueryService>();
        services.AddScoped<CustomerCommandService>();
        services.AddScoped<CustomerExportService>();

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<PlanAdminService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<WhatsAppStatusService>();
        return services;
    }
}
