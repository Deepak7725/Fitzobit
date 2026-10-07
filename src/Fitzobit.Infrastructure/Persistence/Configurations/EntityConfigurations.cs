using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Catalogue;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Imports;
using Fitzobit.Domain.Plans;
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
        b.Property(c => c.Location).HasMaxLength(500);
        b.Property(c => c.NormalizedLocation).HasMaxLength(500);
        b.Property(c => c.Pincode).HasMaxLength(10);
        b.Property(c => c.Locality).HasMaxLength(200);
        b.Property(c => c.City).HasMaxLength(100);
        b.Property(c => c.OriginalPlan).HasMaxLength(300);
        b.Property(c => c.CreatedBy).HasMaxLength(100);
        b.Property(c => c.UpdatedBy).HasMaxLength(100);

        b.HasIndex(c => c.CustomerId).IsUnique();
        b.HasIndex(c => c.NormalizedPhoneNumber);
        b.HasIndex(c => c.Pincode);
        b.HasIndex(c => c.Status);
        b.HasIndex(c => c.CreatedAt);

        b.HasOne(c => c.Plan).WithMany().HasForeignKey(c => c.PlanId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(c => c.PricingTier).WithMany().HasForeignKey(c => c.PricingTierId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ImportBatch>().WithMany().HasForeignKey(c => c.SourceImportBatchId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PlanAliasConfiguration : IEntityTypeConfiguration<PlanAlias>
{
    public void Configure(EntityTypeBuilder<PlanAlias> b)
    {
        b.Property(a => a.AliasKey).HasMaxLength(300).IsRequired();
        b.Property(a => a.OriginalText).HasMaxLength(300).IsRequired();
        b.Property(a => a.CreatedBy).HasMaxLength(100);
        b.HasIndex(a => a.AliasKey).IsUnique();
        b.HasOne(a => a.Plan).WithMany().HasForeignKey(a => a.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(a => a.PricingTier).WithMany().HasForeignKey(a => a.PricingTierId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ImportRowConfiguration : IEntityTypeConfiguration<ImportRow>
{
    public void Configure(EntityTypeBuilder<ImportRow> b)
    {
        // Raw* columns keep the cell text exactly as read; lengths are generous on purpose.
        b.Property(r => r.RawCustomerId).HasMaxLength(500);
        b.Property(r => r.RawCustomerName).HasMaxLength(1000);
        b.Property(r => r.RawPhoneNumber).HasMaxLength(200);
        b.Property(r => r.RawLocation).HasMaxLength(2000);
        b.Property(r => r.RawPlan).HasMaxLength(500);
        b.Property(r => r.CustomerId).HasMaxLength(500);
        b.Property(r => r.CustomerName).HasMaxLength(1000);
        b.Property(r => r.NormalizedPhoneNumber).HasMaxLength(20);
        b.Property(r => r.NormalizedLocation).HasMaxLength(2000);
        b.Property(r => r.Pincode).HasMaxLength(10);
        b.Property(r => r.Locality).HasMaxLength(200);
        b.Property(r => r.City).HasMaxLength(100);
        b.Property(r => r.PlanKey).HasMaxLength(300);
        b.Property(r => r.IssueCodes).HasMaxLength(1000).IsRequired();
        b.Property(r => r.SuggestionReason).HasMaxLength(100);

        b.HasIndex(r => new { r.ImportBatchId, r.RowNumber }).IsUnique();
        b.HasIndex(r => new { r.ImportBatchId, r.Status });
        b.HasIndex(r => new { r.ImportBatchId, r.Pincode });
        b.HasIndex(r => new { r.ImportBatchId, r.PlanKey });

        b.HasOne(r => r.ImportBatch).WithMany(i => i.Rows).HasForeignKey(r => r.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(r => r.TargetDeliveryGroup).WithMany().HasForeignKey(r => r.TargetDeliveryGroupId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(r => r.TargetDraftGroup).WithMany().HasForeignKey(r => r.TargetDraftGroupId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<DeliveryGroup>().WithMany().HasForeignKey(r => r.SuggestedDeliveryGroupId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ImportDraftGroup>().WithMany().HasForeignKey(r => r.SuggestedDraftGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ImportDraftGroupConfiguration : IEntityTypeConfiguration<ImportDraftGroup>
{
    public void Configure(EntityTypeBuilder<ImportDraftGroup> b)
    {
        b.Property(d => d.Name).HasMaxLength(150).IsRequired();
        b.Property(d => d.Description).HasMaxLength(1000);
        b.HasIndex(d => d.ImportBatchId);
        b.HasOne(d => d.ImportBatch).WithMany(i => i.DraftGroups).HasForeignKey(d => d.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<DeliveryGroup>().WithMany().HasForeignKey(d => d.CreatedDeliveryGroupId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ImportPlanMappingConfiguration : IEntityTypeConfiguration<ImportPlanMapping>
{
    public void Configure(EntityTypeBuilder<ImportPlanMapping> b)
    {
        b.Property(m => m.PlanKey).HasMaxLength(300).IsRequired();
        b.Property(m => m.ExampleText).HasMaxLength(300).IsRequired();
        b.Property(m => m.CandidatePlanIdsJson).HasMaxLength(1000);
        b.Property(m => m.Note).HasMaxLength(500);
        b.HasIndex(m => new { m.ImportBatchId, m.PlanKey }).IsUnique();
        b.HasOne(m => m.ImportBatch).WithMany(i => i.PlanMappings).HasForeignKey(m => m.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Plan>().WithMany().HasForeignKey(m => m.PlanId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<CataloguePricingTier>().WithMany().HasForeignKey(m => m.PricingTierId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.Property(p => p.Name).HasMaxLength(300).IsRequired();
        b.Property(p => p.Code).HasMaxLength(100).IsRequired();
        b.Property(p => p.Category).HasMaxLength(100);
        b.Property(p => p.Description).HasMaxLength(1000);

        b.HasIndex(p => p.Code).IsUnique();
        b.HasIndex(p => p.Name).IsUnique();
        b.HasIndex(p => p.Status);

        b.HasOne(p => p.CatalogueItem).WithMany().HasForeignKey(p => p.CatalogueItemId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class CatalogueItemConfiguration : IEntityTypeConfiguration<CatalogueItem>
{
    public void Configure(EntityTypeBuilder<CatalogueItem> b)
    {
        b.Property(i => i.Name).HasMaxLength(300).IsRequired();
        b.Property(i => i.Category).HasMaxLength(100);
        b.Property(i => i.Term).HasMaxLength(100);
        b.Property(i => i.Description).HasMaxLength(4000);
        b.Property(i => i.Currency).HasMaxLength(3).IsRequired();
        b.Property(i => i.DurationText).HasMaxLength(200);
        b.Property(i => i.IncludedItems).HasMaxLength(2000);
        b.Property(i => i.Availability).HasMaxLength(200);
        b.Property(i => i.Source).HasMaxLength(500);
        b.Property(i => i.SourceNotes).HasMaxLength(2000);

        b.HasIndex(i => i.Name).IsUnique();
        b.HasIndex(i => i.Category);

        b.HasMany(i => i.PricingTiers).WithOne(t => t.CatalogueItem)
            .HasForeignKey(t => t.CatalogueItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class CataloguePricingTierConfiguration : IEntityTypeConfiguration<CataloguePricingTier>
{
    public void Configure(EntityTypeBuilder<CataloguePricingTier> b)
    {
        b.Property(t => t.TierName).HasMaxLength(150).IsRequired();
        b.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        b.Property(t => t.Description).HasMaxLength(500);
        b.Property(t => t.Source).HasMaxLength(300);

        b.HasIndex(t => new { t.CatalogueItemId, t.TierName }).IsUnique();
    }
}

internal sealed class DeliveryGroupConfiguration : IEntityTypeConfiguration<DeliveryGroup>
{
    public void Configure(EntityTypeBuilder<DeliveryGroup> b)
    {
        b.Property(g => g.Name).HasMaxLength(150).IsRequired();
        b.Property(g => g.Description).HasMaxLength(1000);
        b.Property(g => g.CreatedBy).HasMaxLength(100);
        b.Property(g => g.UpdatedBy).HasMaxLength(100);

        b.HasIndex(g => g.Name).IsUnique();
        b.HasIndex(g => g.Status);
    }
}

internal sealed class CustomerDeliveryGroupAssignmentConfiguration : IEntityTypeConfiguration<CustomerDeliveryGroupAssignment>
{
    public void Configure(EntityTypeBuilder<CustomerDeliveryGroupAssignment> b)
    {
        b.Property(a => a.AssignedBy).HasMaxLength(100);

        // The filtered unique index on CustomerId (active rows only) is declared in FitzobitDbContext. It enforces the
        // rule but SQLite only uses a partial index when a query repeats its exact filter, so lookups such as
        // "customers without an active group" need these plain composite indexes to avoid a scan per customer.
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

internal sealed class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.Property(i => i.FileName).HasMaxLength(260).IsRequired();
        b.Property(i => i.SheetName).HasMaxLength(100);
        b.Property(i => i.UploadedBy).HasMaxLength(100);
        b.Property(i => i.FailureReason).HasMaxLength(4000);
        b.Property(i => i.FileMessages).HasMaxLength(4000);

        b.HasIndex(i => i.UploadedAt);
        b.HasIndex(i => i.Status);
        b.HasIndex(i => i.CreatedAt);
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
