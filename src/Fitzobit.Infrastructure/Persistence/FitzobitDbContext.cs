using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Common;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Settings;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fitzobit.Infrastructure.Persistence;

public class FitzobitDbContext(DbContextOptions<FitzobitDbContext> options, ICurrentUser currentUser, IClock clock)
    : DbContext(options), IFitzobitDbContext, IDataProtectionKeyContext
{
    /// <summary>Numbers new customer ids (FITZ-000001, …). A database sequence never hands out the same number twice.</summary>
    public const string CustomerNumberSequence = "CustomerNumberSequence";

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerPackage> CustomerPackages => Set<CustomerPackage>();
    public DbSet<PackagePause> PackagePauses => Set<PackagePause>();
    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();
    public DbSet<DictionaryEntry> DictionaryEntries => Set<DictionaryEntry>();
    public DbSet<DeliveryGroup> DeliveryGroups => Set<DeliveryGroup>();
    public DbSet<CustomerDeliveryGroupAssignment> CustomerDeliveryGroupAssignments => Set<CustomerDeliveryGroupAssignment>();
    public DbSet<CustomerDeliveryGroupAssignmentHistory> CustomerDeliveryGroupAssignmentHistory => Set<CustomerDeliveryGroupAssignmentHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Domain.Users.AppUser> Users => Set<Domain.Users.AppUser>();
    public DbSet<Domain.Messaging.WhatsAppMessage> WhatsAppMessages => Set<Domain.Messaging.WhatsAppMessage>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    /// <summary>ASP.NET Core data-protection keys (sign-in and CSRF cookies), so sessions survive restarts on hosts without a lasting disk.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FitzobitDbContext).Assembly);
        modelBuilder.HasSequence<long>(CustomerNumberSequence);

        // "One active delivery group per customer" is also enforced by the database.
        modelBuilder.Entity<CustomerDeliveryGroupAssignment>()
            .HasIndex(a => a.CustomerId)
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE")
            .HasDatabaseName("UX_CustomerDeliveryGroupAssignments_ActiveCustomer");
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as readable text, money with fixed precision, and every DateTime round-tripped as UTC (DateOnly maps to "date").
        configurationBuilder.Properties<RecordStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<DictionaryType>().HaveConversion<string>().HaveMaxLength(30);
        configurationBuilder.Properties<GeocodeStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<GeoMatchLevel>().HaveConversion<string>().HaveMaxLength(20);
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
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
