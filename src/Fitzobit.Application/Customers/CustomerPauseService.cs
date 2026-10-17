using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Dictionaries;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Customers;

public sealed class PauseCustomerRequest
{
    /// <summary>The delivery day the customer will not take.</summary>
    public DateOnly Date { get; set; }

    public string? Reason { get; set; }
}

public sealed record PausedPackageDto(int PackageId, string Package, DateOnly PreviousEndDate, DateOnly NewEndDate, int PausedCount, int PausesLeft);

public sealed record PauseResultDto(DateOnly PausedDate, IReadOnlyList<PausedPackageDto> Packages);

public sealed record PackagePauseDto(DateOnly PausedDate, DateOnly PreviousEndDate, DateOnly NewEndDate, string? Reason, DateTime CreatedAt, string? CreatedBy);

/// <summary>
/// Pauses one delivery day of a customer: every package running that day is paused and each one's end date moves to the next
/// delivery day (skipping Sundays), so no meal is lost. At most <see cref="CustomerPackage.MaxPauses"/> pauses per package; if any
/// running package has none left, the day cannot be paused. Only active customers, only today or later. The pauses, new end
/// dates and counts are saved together in one transaction; package row versions and the unique (package, date) index stop a
/// repeated or concurrent request from counting twice.
/// </summary>
public sealed class CustomerPauseService(IFitzobitDbContext db, DeliveryScheduleService schedule, IAuditLogger audit, ICurrentUser currentUser, IClock clock)
{
    public const int MaxReasonLength = 300;

    public async Task<PauseResultDto> PauseAsync(int customerId, PauseCustomerRequest request, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().Where(c => c.Id == customerId)
                           .Select(c => new { c.Id, c.CustomerId, StatusCode = c.Status!.Code, Status = c.Status.DisplayName }).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"Customer {customerId} was not found.");
        if (customer.StatusCode != CustomerStatusCodes.Active)
            throw new BusinessRuleException(
                $"{customer.CustomerId} is {customer.Status}. Only active customers can pause a delivery; change the status to Active first.");

        var date = request.Date;
        var calendar = schedule.Calendar;
        if (!calendar.IsDeliveryDay(date)) throw new BusinessRuleException($"{date:dd MMM yyyy} is a Sunday, which is not a delivery day.");
        if (date < DeliveryScheduleService.Today(clock)) throw new BusinessRuleException("Only today or a future delivery day can be paused.");

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        if (reason?.Length > MaxReasonLength) throw new BusinessRuleException($"The reason must be at most {MaxReasonLength} characters.");

        var running = await db.CustomerPackages.Include(p => p.Pauses).Include(p => p.Preference).Include(p => p.MealType).Include(p => p.Frequency)
            .Where(p => p.CustomerId == customerId && p.StartDate <= date && p.EndDate >= date)
            .OrderBy(p => p.StartDate).ThenBy(p => p.Id)
            .ToListAsync(ct);
        if (running.Count == 0) throw new BusinessRuleException($"{customer.CustomerId} has no package delivering on {date:dd MMM yyyy}.");

        var toPause = running.Where(p => p.Pauses.All(x => x.PausedDate != date)).ToList();
        if (toPause.Count == 0) throw new BusinessRuleException($"{date:dd MMM yyyy} is already paused.");

        var full = toPause.Where(p => p.PausedCount >= CustomerPackage.MaxPauses).ToList();
        if (full.Count > 0)
            throw new BusinessRuleException(
                $"{date:dd MMM yyyy} cannot be paused: {string.Join("; ", full.Select(Label))} already used {(full.Count == 1 ? "its" : "their")} " +
                $"{CustomerPackage.MaxPauses} pauses.");

        var results = new List<PausedPackageDto>();
        foreach (var package in toPause)
        {
            var previousEnd = package.EndDate;
            var newEnd = calendar.ExtendEndDateForPause(previousEnd);
            package.Pauses.Add(new PackagePause
            {
                PausedDate = date,
                PreviousEndDate = previousEnd,
                NewEndDate = newEnd,
                Reason = reason,
                CreatedAt = clock.UtcNow,
                CreatedBy = currentUser.UserName
            });
            package.PausedCount++;
            package.EndDate = newEnd;
            results.Add(new PausedPackageDto(package.Id, Label(package), previousEnd, newEnd, package.PausedCount,
                CustomerPackage.MaxPauses - package.PausedCount));
        }

        audit.RecordChange(AuditActions.CustomerPaused, nameof(Customer), customer.CustomerId,
            $"Delivery paused: {customer.CustomerId} on {date:dd MMM yyyy} ({results.Count} package(s))",
            results.ToDictionary(r => $"#{r.PackageId} {r.Package}", r => (object?)new { EndDate = r.PreviousEndDate, PausedCount = r.PausedCount - 1 }),
            results.ToDictionary(r => $"#{r.PackageId} {r.Package}", r => (object?)new { EndDate = r.NewEndDate, r.PausedCount }),
            new { PausedDate = date, Reason = reason });
        await db.SaveChangesAsync(ct);
        return new PauseResultDto(date, results);
    }

    /// <summary>"Veg Medium ×1 · Monthly (12 Oct 2026 – 10 Nov 2026)"</summary>
    private static string Label(CustomerPackage p) =>
        CustomerPackageService.Text(p.Preference!.DisplayName, p.MealType!.DisplayName, p.Quantity, p.Frequency!.DisplayName, p.StartDate, p.EndDate);
}
