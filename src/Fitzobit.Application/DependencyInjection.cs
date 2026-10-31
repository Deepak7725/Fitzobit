using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Administration;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Customers;
using Fitzobit.Application.Dashboard;
using Fitzobit.Application.DeliveryGroups;
using Fitzobit.Application.Classification;
using Fitzobit.Application.Dictionaries;
using Fitzobit.Application.Geocoding;
using Fitzobit.Application.Messaging;
using Fitzobit.Application.Users;
using Fitzobit.Domain.Deliveries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fitzobit.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDeliveryCalendarService, DeliveryCalendarService>();
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<DashboardService>();
        services.AddScoped<DeliveryGroupAssignmentService>();
        services.AddScoped<DeliveryGroupQueryService>();
        services.AddScoped<DeliveryGroupService>();
        services.AddScoped<DictionaryService>();

        services.AddSingleton<ILocationClassificationService>(sp =>
            new LocationClassificationService(
                sp.GetService<Microsoft.Extensions.Options.IOptions<LocationClassificationOptions>>()?.Value ?? new LocationClassificationOptions()));
        services.AddScoped<DeliveryScheduleService>();
        services.AddScoped<CustomerQueryService>();
        services.AddScoped<CustomerCommandService>();
        services.AddScoped<CustomerPauseService>();
        services.AddScoped<CustomerAutomationService>();
        services.AddScoped<RenewalReminderService>();
        services.AddScoped<DailyOrders.DailyOrdersService>();
        services.AddScoped<CustomerPackageService>();
        services.AddScoped<CustomerNoteService>();
        services.AddScoped<CustomerExportService>();

        services.TryAddSingleton<IGeocodingQueue, GeocodingQueue>();
        services.AddScoped<ServiceAreaService>();
        services.AddScoped<CustomerGeocodingService>();
        services.AddScoped<ClassificationService>();

        services.AddScoped<AuthService>();
        services.AddScoped<UserService>();
        services.AddScoped<AuditQueryService>();
        services.AddScoped<WhatsAppStatusService>();
        return services;
    }
}
