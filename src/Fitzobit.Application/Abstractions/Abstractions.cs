using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Imports;
using Fitzobit.Domain.Plans;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fitzobit.Application.Abstractions;

/// <summary>
/// Persistence boundary used by application services. Deliberately provider-neutral:
/// nothing here (or in callers) may depend on SQLite- or PostgreSQL-specific behaviour.
/// </summary>
public interface IFitzobitDbContext
{
    DbSet<Customer> Customers { get; }
    DbSet<Plan> Plans { get; }
    DbSet<PlanAlias> PlanAliases { get; }
    DbSet<CatalogueItem> CatalogueItems { get; }
    DbSet<CataloguePricingTier> CataloguePricingTiers { get; }
    DbSet<DeliveryGroup> DeliveryGroups { get; }
    DbSet<CustomerDeliveryGroupAssignment> CustomerDeliveryGroupAssignments { get; }
    DbSet<CustomerDeliveryGroupAssignmentHistory> CustomerDeliveryGroupAssignmentHistory { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<ImportRow> ImportRows { get; }
    DbSet<ImportDraftGroup> ImportDraftGroups { get; }
    DbSet<ImportPlanMapping> ImportPlanMappings { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Domain.Users.AppUser> Users { get; }
    DbSet<Domain.Messaging.WhatsAppMessage> WhatsAppMessages { get; }

    /// <summary>For transactions only (BeginTransactionAsync); provider-neutral.</summary>
    DatabaseFacade Database { get; }

    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>The user performing the current operation. Part 1 has no sign-in, so this comes from configuration.</summary>
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
