using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fitzobit.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.Property(c => c.CustomerId).HasMaxLength(50).IsRequired();
        b.Property(c => c.CustomerName).HasMaxLength(200).IsRequired();
        b.Property(c => c.PhoneNumber).HasMaxLength(30).IsRequired();
        b.Property(c => c.NormalizedPhoneNumber).HasMaxLength(20);
        b.Property(c => c.AlternatePhoneNumber).HasMaxLength(30);
        b.Property(c => c.Address).HasMaxLength(500);
        b.Property(c => c.NormalizedAddress).HasMaxLength(500);
        b.Property(c => c.Pincode).HasMaxLength(10);
        b.Property(c => c.GoogleMapUrl).HasMaxLength(2000); // CustomerCommandService.MaxGoogleMapUrlLength
        b.Property(c => c.Locality).HasMaxLength(200);
        b.Property(c => c.City).HasMaxLength(100);
        b.Property(c => c.Latitude).HasPrecision(9, 6);
        b.Property(c => c.Longitude).HasPrecision(9, 6);
        b.Property(c => c.LegacyPlan).HasMaxLength(500);
        b.Property(c => c.GeocodeMessage).HasMaxLength(300);
        b.Property(c => c.GeocodeSource).HasMaxLength(30);
        b.Property(c => c.Version).IsRowVersion();
        b.Property(c => c.CreatedBy).HasMaxLength(100);
        b.Property(c => c.UpdatedBy).HasMaxLength(100);

        b.HasIndex(c => c.CustomerId).IsUnique();
        b.HasIndex(c => c.NormalizedPhoneNumber);
        b.HasIndex(c => c.Pincode);
        b.HasIndex(c => new { c.GeocodeStatus, c.GeocodeNextAttemptAt });
        b.HasIndex(c => c.CreatedAt);

        // Drop-down values are never deleted while customers use them.
        b.HasOne(c => c.Status).WithMany().HasForeignKey(c => c.StatusId).OnDelete(DeleteBehavior.Restrict);

        b.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Customers_Latitude", "\"Latitude\" IS NULL OR \"Latitude\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_Customers_Longitude", "\"Longitude\" IS NULL OR \"Longitude\" BETWEEN -180 AND 180");
        });
    }
}

internal sealed class CustomerPackageConfiguration : IEntityTypeConfiguration<CustomerPackage>
{
    public void Configure(EntityTypeBuilder<CustomerPackage> b)
    {
        b.ToTable("CustomerPackages", t =>
        {
            t.HasCheckConstraint("CK_CustomerPackages_Quantity", $"\"Quantity\" BETWEEN 1 AND {CustomerPackage.MaxQuantity}");
            t.HasCheckConstraint("CK_CustomerPackages_PausedCount", "\"PausedCount\" >= 0");
            t.HasCheckConstraint("CK_CustomerPackages_Dates", "\"EndDate\" >= \"StartDate\"");
            t.HasCheckConstraint("CK_CustomerPackages_Price", "\"Price\" IS NULL OR \"Price\" >= 0");
        });
        b.Property(p => p.Version).IsRowVersion();
        b.Property(p => p.CreatedBy).HasMaxLength(100);
        b.Property(p => p.UpdatedBy).HasMaxLength(100);
        b.Property(p => p.RenewalReminderBy).HasMaxLength(100);
        b.HasIndex(p => new { p.CustomerId, p.StartDate });
        b.HasIndex(p => p.EndDate);
        b.HasIndex(p => p.ArchivedAt);
        b.HasOne(p => p.Customer).WithMany(c => c.Packages).HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        // Drop-down values are never deleted while packages use them.
        b.HasOne(p => p.Preference).WithMany().HasForeignKey(p => p.PreferenceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.MealType).WithMany().HasForeignKey(p => p.MealTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Frequency).WithMany().HasForeignKey(p => p.FrequencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PackagePauseConfiguration : IEntityTypeConfiguration<PackagePause>
{
    public void Configure(EntityTypeBuilder<PackagePause> b)
    {
        b.ToTable("PackagePauses");
        b.Property(p => p.Reason).HasMaxLength(300);
        b.Property(p => p.CreatedBy).HasMaxLength(100);
        b.HasIndex(p => new { p.PackageId, p.PausedDate }).IsUnique();
        b.HasIndex(p => p.PausedDate);
        // Deleting a package (only allowed before its first delivery) removes its pauses with it.
        b.HasOne(p => p.Package).WithMany(k => k.Pauses).HasForeignKey(p => p.PackageId).OnDelete(DeleteBehavior.Cascade);
        // Removing a festive pause undoes its package pauses first (end dates move back), then deletes it.
        b.HasOne(p => p.FestivePause).WithMany(f => f.Pauses).HasForeignKey(p => p.FestivePauseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FestivePauseConfiguration : IEntityTypeConfiguration<FestivePause>
{
    public void Configure(EntityTypeBuilder<FestivePause> b)
    {
        b.ToTable("FestivePauses");
        b.Property(f => f.Reason).HasMaxLength(300);
        b.Property(f => f.CreatedBy).HasMaxLength(100);
        b.HasIndex(f => f.Date).IsUnique();
    }
}

internal sealed class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> b)
    {
        b.ToTable("CustomerNotes");
        b.Property(n => n.Text).HasMaxLength(CustomerNote.MaxLength).IsRequired();
        b.Property(n => n.CreatedBy).HasMaxLength(100);
        b.Property(n => n.UpdatedBy).HasMaxLength(100);
        b.HasIndex(n => new { n.CustomerId, n.CreatedAt });
        b.HasOne(n => n.Customer).WithMany(c => c.Notes).HasForeignKey(n => n.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DictionaryEntryConfiguration : IEntityTypeConfiguration<DictionaryEntry>
{
    public void Configure(EntityTypeBuilder<DictionaryEntry> b)
    {
        b.Property(e => e.Code).HasMaxLength(50).IsRequired();
        b.Property(e => e.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(e => e.CreatedBy).HasMaxLength(100);
        b.Property(e => e.UpdatedBy).HasMaxLength(100);
        b.HasIndex(e => new { e.DictionaryType, e.Code }).IsUnique();
        b.HasIndex(e => new { e.DictionaryType, e.SortOrder });
    }
}

internal sealed class DeliveryGroupConfiguration : IEntityTypeConfiguration<DeliveryGroup>
{
    public void Configure(EntityTypeBuilder<DeliveryGroup> b)
    {
        b.Property(g => g.Description).HasMaxLength(1000);
        b.Property(g => g.CreatedBy).HasMaxLength(100);
        b.Property(g => g.UpdatedBy).HasMaxLength(100);

        b.HasIndex(g => g.GroupNumber).IsUnique();
        b.HasIndex(g => g.Status);
        b.ToTable(t => t.HasCheckConstraint("CK_DeliveryGroups_GroupNumber", "\"GroupNumber\" > 0"));
    }
}

internal sealed class PackagePriceConfiguration : IEntityTypeConfiguration<Domain.Money.PackagePrice>
{
    public void Configure(EntityTypeBuilder<Domain.Money.PackagePrice> b)
    {
        b.ToTable("PackagePrices", t => t.HasCheckConstraint("CK_PackagePrices_Price", "\"Price\" > 0"));
        b.Property(p => p.CreatedBy).HasMaxLength(100);
        b.Property(p => p.UpdatedBy).HasMaxLength(100);
        b.HasIndex(p => new { p.MealTypeId, p.PreferenceId, p.FrequencyId }).IsUnique();
        b.HasOne(p => p.MealType).WithMany().HasForeignKey(p => p.MealTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Preference).WithMany().HasForeignKey(p => p.PreferenceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(p => p.Frequency).WithMany().HasForeignKey(p => p.FrequencyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.HasKey(s => s.Key);
        b.Property(s => s.Key).HasMaxLength(100);
        b.Property(s => s.Value).HasMaxLength(2000);
        b.Property(s => s.UpdatedBy).HasMaxLength(100);
    }
}

internal sealed class CustomerDeliveryGroupAssignmentConfiguration : IEntityTypeConfiguration<CustomerDeliveryGroupAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerDeliveryGroupAssignment> b)
    {
        b.Property(a => a.AssignedBy).HasMaxLength(100);

        // The filtered unique index on CustomerId (active rows only) is declared in FitzobitDbContext. It enforces the
        // rule; lookups such as "customers without an active group" use these plain composite indexes.
        b.HasIndex(a => new { a.CustomerId, a.IsActive });
        b.HasIndex(a => new { a.DeliveryGroupId, a.IsActive });

        b.HasOne(a => a.Customer).WithMany(c => c.GroupAssignments)
            .HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(a => a.DeliveryGroup).WithMany(g => g.Assignments)
            .HasForeignKey(a => a.DeliveryGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CustomerDeliveryGroupAssignmentHistoryConfiguration : IEntityTypeConfiguration<CustomerDeliveryGroupAssignmentHistory>
{
    public void Configure(EntityTypeBuilder<CustomerDeliveryGroupAssignmentHistory> b)
    {
        b.ToTable("CustomerDeliveryGroupAssignmentHistory");
        b.Property(h => h.ChangedBy).HasMaxLength(100);
        b.Property(h => h.Reason).HasMaxLength(500);

        b.HasIndex(h => h.CustomerId);
        b.HasIndex(h => h.ChangedAt);

        b.HasOne(h => h.Customer).WithMany().HasForeignKey(h => h.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(h => h.PreviousGroup).WithMany().HasForeignKey(h => h.PreviousGroupId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(h => h.NewGroup).WithMany().HasForeignKey(h => h.NewGroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<Domain.Users.AppUser>
{
    public void Configure(EntityTypeBuilder<Domain.Users.AppUser> b)
    {
        b.ToTable("Users");
        b.Property(u => u.UserName).HasMaxLength(50).IsRequired();
        b.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        b.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        b.Property(u => u.SecurityStamp).HasMaxLength(64).IsRequired();
        b.HasIndex(u => u.UserName).IsUnique();
    }
}

internal sealed class WhatsAppMessageConfiguration : IEntityTypeConfiguration<Domain.Messaging.WhatsAppMessage>
{
    public void Configure(EntityTypeBuilder<Domain.Messaging.WhatsAppMessage> b)
    {
        b.Property(m => m.ToPhoneNumber).HasMaxLength(20).IsRequired();
        b.Property(m => m.TemplateName).HasMaxLength(200).IsRequired();
        b.Property(m => m.LanguageCode).HasMaxLength(20).IsRequired();
        b.Property(m => m.ProviderMessageId).HasMaxLength(200);
        b.Property(m => m.Error).HasMaxLength(1000);
        b.Property(m => m.CreatedBy).HasMaxLength(100);
        b.HasIndex(m => m.CustomerId);
        b.HasIndex(m => m.CreatedAt);
        b.HasIndex(m => m.ProviderMessageId);
        b.HasOne<Customer>().WithMany().HasForeignKey(m => m.CustomerId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<DeliveryGroup>().WithMany().HasForeignKey(m => m.DeliveryGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasIndex(a => a.Action);
        b.Property(a => a.Action).HasMaxLength(60).IsRequired();
        b.Property(a => a.EntityType).HasMaxLength(100).IsRequired();
        b.Property(a => a.EntityKey).HasMaxLength(300);
        b.Property(a => a.Summary).HasMaxLength(1000).IsRequired();
        b.Property(a => a.PerformedBy).HasMaxLength(100);

        b.HasIndex(a => a.OccurredAt);
        b.HasIndex(a => new { a.EntityType, a.EntityKey });
    }
}
