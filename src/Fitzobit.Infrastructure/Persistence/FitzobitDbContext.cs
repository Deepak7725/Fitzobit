using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Imports;
using Fitzobit.Domain.Plans;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fitzobit.Infrastructure.Persistence;

public class FitzobitDbContext(DbContextOptions<FitzobitDbContext> options, ICurrentUser currentUser, IClock clock)
    : DbContext(options), IFitzobitDbContext, IDataProtectionKeyContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanAlias> PlanAliases => Set<PlanAlias>();
    public DbSet<CatalogueItem> CatalogueItems => Set<CatalogueItem>();
    public DbSet<CataloguePricingTier> CataloguePricingTiers => Set<CataloguePricingTier>();
    public DbSet<DeliveryGroup> DeliveryGroups => Set<DeliveryGroup>();
    public DbSet<CustomerDeliveryGroupAssignment> CustomerDeliveryGroupAssignments => Set<CustomerDeliveryGroupAssignment>();
    public DbSet<CustomerDeliveryGroupAssignmentHistory> CustomerDeliveryGroupAssignmentHistory => Set<CustomerDeliveryGroupAssignmentHistory>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportRow> ImportRows => Set<ImportRow>();
    public DbSet<ImportDraftGroup> ImportDraftGroups => Set<ImportDraftGroup>();
    public DbSet<ImportPlanMapping> ImportPlanMappings => Set<ImportPlanMapping>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Domain.Users.AppUser> Users => Set<Domain.Users.AppUser>();
    public DbSet<Domain.Messaging.WhatsAppMessage> WhatsAppMessages => Set<Domain.Messaging.WhatsAppMessage>();

    /// <summary>ASP.NET Core data-protection keys (sign-in and CSRF cookies), so sessions survive restarts on hosts without a lasting disk.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FitzobitDbContext).Assembly);

        // "One active delivery group per customer" is also enforced by the database. Partial-index
        // syntax for booleans differs per provider, so it is the only provider-aware line in the model.
        var activeFilter = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? "\"IsActive\" = 1"
            : "\"IsActive\" = TRUE";
        modelBuilder.Entity<CustomerDeliveryGroupAssignment>()
            .HasIndex(a => a.CustomerId)
            .IsUnique()
            .HasFilter(activeFilter)
            .HasDatabaseName("UX_CustomerDeliveryGroupAssignments_ActiveCustomer");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as readable text, money with fixed precision, and every DateTime round-tripped as UTC.
        configurationBuilder.Properties<RecordStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<CustomerStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ImportBatchStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<BillingPeriod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ImportRowStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PlanMatchMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PlanMappingStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Domain.Users.UserRole>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Domain.Messaging.WhatsAppMessagePurpose>().HaveConversion<string>().HaveMaxLength(40);
        configurationBuilder.Properties<Domain.Messaging.WhatsAppMessageStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<decimal>().HavePrecision(12, 2);
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampChanges()
    {
        var now = clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITimestamped stamped)
            {
                if (entry.State == EntityState.Added)
                {
                    if (stamped.CreatedAt == default) stamped.CreatedAt = now;
                    stamped.UpdatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    stamped.UpdatedAt = now;
                }
            }

            if (entry.Entity is IUserTracked tracked)
            {
                if (entry.State == EntityState.Added)
                {
                    tracked.CreatedBy ??= currentUser.UserName;
                    tracked.UpdatedBy = currentUser.UserName;
                }
                else if (entry.State == EntityState.Modified)
                {
                    tracked.UpdatedBy = currentUser.UserName;
                }
            }

            if (entry is { State: EntityState.Added, Entity: ImportBatch batch } && batch.CreatedAt == default)
                batch.CreatedAt = now;
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
