using System.Data.Common;
using Fitzobit.Application.Common;
using Fitzobit.Application.Imports;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Imports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fitzobit.Tests.Imports;

public class ImportCommitTests
{
    private const string Regular6 = "Regular Fit Box - 6 Days";

    /// <summary>Two new customers, one existing customer (moving groups), one error row and one plan to map manually.</summary>
    private static async Task<(ImportHarness Harness, int BatchId, int OldGroupId)> PrepareAsync(TestDatabase test)
    {
        var harness = await ImportHarness.CreateAsync(test);
        int oldGroupId;
        await using (var db = test.CreateContext())
        {
            var oldGroup = new DeliveryGroup { Name = "Old Route" };
            var existing = new Customer { CustomerId = "CUST900", CustomerName = "Saved Name", Pincode = "600040" };
            existing.SetPhoneNumber("9811111111");
            existing.SetLocation("Saved address, Chennai - 600040");
            db.AddRange(oldGroup, existing);
            await db.SaveChangesAsync();
            db.CustomerDeliveryGroupAssignments.Add(new CustomerDeliveryGroupAssignment { CustomerId = existing.Id, DeliveryGroupId = oldGroup.Id, AssignedAt = test.Clock.UtcNow });
            await db.SaveChangesAsync();
            oldGroupId = oldGroup.Id;
        }

        using var file = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["N1", "New One", "+91 98765 00001", "RS Puram, Coimbatore - 641002", Regular6],
            ["N2", "New Two", 9876500002d, "Gandhipuram, Coimbatore - 641012", "Big Box Veg"],
            ["CUST900", "Name In File", "9811111111", "Different address, Chennai - 600040", Regular6],
            [null, "Broken Row", "123", "", Regular6]);
        var batchId = await harness.UploadAsync(file, "october.xlsx");

        await using (var db = test.CreateContext())
        {
            var wf = harness.Workflow(db);
            var mapping = (await harness.Queries(db).GetPlanMappingsAsync(batchId)).Single(m => m.Status != "Mapped");
            var large = await db.Plans.SingleAsync(p => p.Name == "Large Fit Box - 6 Days");
            await wf.UpdatePlanMappingAsync(batchId, mapping.Id, large.Id, null);

            var group = await wf.CreateDraftGroupAsync(batchId, "Coimbatore North", "RS Puram and around");
            await wf.AssignAsync(batchId, new ImportRowSelection { Filter = new ImportRowFilter { Status = "Included" } }, $"d:{group.Id}");
        }
        return (harness, batchId, oldGroupId);
    }

    [Fact]
    public async Task Confirm_and_save_writes_everything_in_one_go()
    {
        using var test = new TestDatabase();
        var (harness, batchId, oldGroupId) = await PrepareAsync(test);

        await using (var db = test.CreateContext())
        {
            var review = await harness.Queries(db).GetReviewAsync(batchId);
            Assert.True(review.CanSave);
            Assert.Equal(3, review.Summary.ToImport);
            Assert.Equal(1, review.GroupChangesForExistingCustomers);

            var result = await harness.Commit(db).CommitAsync(batchId);
            Assert.Equal((2, 1, 1, 3, 3, 1), (result.CustomersCreated, result.ExistingCustomersMatched, result.GroupsCreated,
                result.AssignmentsChanged, result.ImportedCount, result.SkippedRows));
        }

        await using var check = test.CreateContext();
        var group = await check.DeliveryGroups.SingleAsync(g => g.Name == "Coimbatore North");
        Assert.Equal("RS Puram and around", group.Description);

        var n1 = await check.Customers.Include(c => c.Plan).SingleAsync(c => c.CustomerId == "N1");
        Assert.Equal("+91 98765 00001", n1.PhoneNumber);           // original kept
        Assert.Equal("9876500001", n1.NormalizedPhoneNumber);
        Assert.Equal("RS Puram, Coimbatore - 641002", n1.Location); // original kept
        Assert.Equal("641002", n1.Pincode);
        Assert.Equal(Regular6, n1.Plan!.Name);
        Assert.Equal(Regular6, n1.OriginalPlan);
        Assert.Equal(batchId, n1.SourceImportBatchId);

        var n2 = await check.Customers.Include(c => c.Plan).SingleAsync(c => c.CustomerId == "N2");
        Assert.Equal("9876500002", n2.PhoneNumber);                  // numeric cell kept as full digits
        Assert.Equal("Big Box Veg", n2.OriginalPlan);
        Assert.Equal("Large Fit Box - 6 Days", n2.Plan!.Name);

        // Existing customer: details untouched, but moved to the new group with history.
        var existing = await check.Customers.SingleAsync(c => c.CustomerId == "CUST900");
        Assert.Equal("Saved Name", existing.CustomerName);
        Assert.Equal("Saved address, Chennai - 600040", existing.Location);
        var active = await check.CustomerDeliveryGroupAssignments.Where(a => a.CustomerId == existing.Id && a.IsActive).ToListAsync();
        Assert.Equal(group.Id, Assert.Single(active).DeliveryGroupId);
        var move = await check.CustomerDeliveryGroupAssignmentHistory.SingleAsync(h => h.CustomerId == existing.Id);
        Assert.Equal((oldGroupId, group.Id, "test-user"), (move.PreviousGroupId!.Value, move.NewGroupId!.Value, move.ChangedBy!));
        Assert.Contains("october.xlsx", move.Reason);

        Assert.Equal(3, await check.CustomerDeliveryGroupAssignmentHistory.CountAsync());
        Assert.Equal(1, await check.PlanAliases.CountAsync());        // the manual "Big Box Veg" mapping is remembered

        var batch = await check.ImportBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(ImportBatchStatus.Completed, batch.Status);
        Assert.Equal(3, batch.ImportedCount);
        Assert.NotNull(batch.CompletedAt);

        var actions = await check.AuditLogs.Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.ImportPerformed, actions);
        Assert.Contains(AuditActions.GroupCreated, actions);
        Assert.Equal(2, actions.Count(a => a == AuditActions.CustomerCreated));
        Assert.Contains(AuditActions.CustomerReassigned, actions);
    }

    [Fact]
    public async Task A_failure_during_save_rolls_everything_back()
    {
        using var test = new TestDatabase();
        var (harness, batchId, oldGroupId) = await PrepareAsync(test);
        int customersBefore, historyBefore, groupsBefore;
        await using (var db = test.CreateContext())
        {
            customersBefore = await db.Customers.CountAsync();
            historyBefore = await db.CustomerDeliveryGroupAssignmentHistory.CountAsync();
            groupsBefore = await db.DeliveryGroups.CountAsync();
        }

        // Fail on the 3rd SaveChanges inside the transaction: groups and customers have already been written by then.
        await using (var db = test.CreateContext(new FailOnSaveInterceptor(failOnCall: 3)))
        {
            var error = await Assert.ThrowsAsync<BusinessRuleException>(() => harness.Commit(db).CommitAsync(batchId));
            Assert.Contains("rolled back", error.Message);
        }

        await using var check = test.CreateContext();
        Assert.Equal(customersBefore, await check.Customers.CountAsync());
        Assert.Equal(groupsBefore, await check.DeliveryGroups.CountAsync());
        Assert.Equal(historyBefore, await check.CustomerDeliveryGroupAssignmentHistory.CountAsync());
        Assert.Equal(0, await check.PlanAliases.CountAsync());
        Assert.Equal(oldGroupId, (await check.CustomerDeliveryGroupAssignments.SingleAsync(a => a.IsActive)).DeliveryGroupId);

        var batch = await check.ImportBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(ImportBatchStatus.Validated, batch.Status);       // still a draft, can be retried
        Assert.Contains("rolled back", batch.FailureReason);

        // Retrying without the fault succeeds.
        await using var retry = test.CreateContext();
        var result = await harness.Commit(retry).CommitAsync(batchId);
        Assert.Equal(2, result.CustomersCreated);
    }

    [Fact]
    public async Task Save_is_refused_when_a_customer_id_was_taken_after_validation()
    {
        using var test = new TestDatabase();
        var (harness, batchId, _) = await PrepareAsync(test);
        await using (var db = test.CreateContext())
        {
            var late = new Customer { CustomerId = "n1", CustomerName = "Someone Else" };
            late.SetPhoneNumber("9000000099");
            db.Customers.Add(late);
            await db.SaveChangesAsync();
        }

        await using var save = test.CreateContext();
        var error = await Assert.ThrowsAsync<BusinessRuleException>(() => harness.Commit(save).CommitAsync(batchId));
        Assert.Contains("n1", error.Message);
        Assert.Equal(ImportBatchStatus.Validated, (await save.ImportBatches.SingleAsync(b => b.Id == batchId)).Status);
    }

    [Fact]
    public async Task Re_uploaded_customers_keep_their_group_and_are_not_counted_as_unassigned()
    {
        using var test = new TestDatabase();
        var (harness, batchId, _) = await PrepareAsync(test);
        await using (var db = test.CreateContext()) await harness.Commit(db).CommitAsync(batchId);

        using var again = TestWorkbook.Create(TestWorkbook.StandardHeaders,
            ["N1", "New One", "+91 98765 00001", "RS Puram, Coimbatore - 641002", Regular6],
            ["N9", "Brand New", "9876500009", "Gandhipuram, Coimbatore - 641012", Regular6]);
        var secondId = await harness.UploadAsync(again, "november.xlsx");

        await using var check = test.CreateContext();
        var summary = await harness.Queries(check).GetSummaryAsync(secondId);
        Assert.Equal((2, 1, 1), (summary.ToImport, summary.KeepingCurrentGroup, summary.Unassigned));
    }

    [Fact]
    public async Task A_saved_import_cannot_be_saved_or_edited_again()
    {
        using var test = new TestDatabase();
        var (harness, batchId, _) = await PrepareAsync(test);
        await using var db = test.CreateContext();
        await harness.Commit(db).CommitAsync(batchId);

        await Assert.ThrowsAsync<BusinessRuleException>(() => harness.Commit(db).CommitAsync(batchId));
        await Assert.ThrowsAsync<BusinessRuleException>(() => harness.Workflow(db).CreateDraftGroupAsync(batchId, "Late", null));
    }

    private sealed class FailOnSaveInterceptor(int failOnCall) : SaveChangesInterceptor
    {
        private int _calls;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (++_calls == failOnCall) throw new SimulatedDatabaseException();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class SimulatedDatabaseException() : DbException("Simulated database failure");
}
