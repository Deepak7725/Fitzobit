using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fitzobit.Application.Abstractions;

/// <summary>
/// Persistence boundary used by application services (PostgreSQL through EF Core). Provider-specific SQL stays in
/// Infrastructure (e.g. <see cref="Customers.ICustomerIdGenerator"/>).
/// </summary>
public interface IFitzobitDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<CustomerPackage> CustomerPackages { get; }
    DbSet<PackagePause> PackagePauses { get; }
    DbSet<FestivePause> FestivePauses { get; }
    DbSet<CustomerNote> CustomerNotes { get; }
    DbSet<DictionaryEntry> DictionaryEntries { get; }
    DbSet<DeliveryGroup> DeliveryGroups { get; }
    DbSet<CustomerDeliveryGroupAssignment> CustomerDeliveryGroupAssignments { get; }
    DbSet<CustomerDeliveryGroupAssignmentHistory> CustomerDeliveryGroupAssignmentHistory { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Domain.Users.AppUser> Users { get; }
    DbSet<Domain.Messaging.WhatsAppMessage> WhatsAppMessages { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<Domain.Money.PackagePrice> PackagePrices { get; }

    /// <summary>For transactions only (BeginTransactionAsync).</summary>
    DatabaseFacade Database { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The user performing the current operation (the signed-in user).</summary>
public interface ICurrentUser
{
    string UserName { get; }
    string DisplayName { get; }
    string Role { get; }
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
