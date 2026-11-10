using System.Globalization;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Auditing;
using Fitzobit.Application.Common;
using Fitzobit.Application.Customers;
using Fitzobit.Domain.Auditing;
using Fitzobit.Domain.Customers;
using Fitzobit.Domain.Deliveries;
using Fitzobit.Domain.DeliveryGroups;
using Fitzobit.Domain.Dictionaries;
using Fitzobit.Domain.Money;
using Fitzobit.Domain.Settings;
using Fitzobit.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Money;

/// <param name="Percent">Target profit as a percentage of the selling price (owner: Monthly 30, Weekly 40).</param>
/// <param name="DeliveryDays">Deliveries in a package of this frequency (26, 6, 1 …).</param>
public sealed record ProfitTargetDto(int FrequencyId, string Frequency, int? DeliveryDays, decimal Percent);

/// <param name="DeliveryCostPerBox">Delivery person + petrol, per box delivered.</param>
/// <param name="HelperCostPerBox">Helper, per box delivered.</param>
public sealed record MoneySettingsDto(decimal DeliveryCostPerBox, decimal HelperCostPerBox, IReadOnlyList<ProfitTargetDto> Targets, DateTime? UpdatedAt,
    string? UpdatedBy);

public sealed class SaveProfitTarget
{
    public int FrequencyId { get; set; }
    public decimal Percent { get; set; }
}

public sealed class SaveMoneySettingsRequest
{
    public decimal DeliveryCostPerBox { get; set; }
    public decimal HelperCostPerBox { get; set; }
    public List<SaveProfitTarget> Targets { get; set; } = [];
}

public sealed record PriceOptionDto(int Id, string Name, int? DeliveryDays = null);

/// <param name="Price">Package price for one box per delivery day (quantity 1).</param>
public sealed record PriceCellDto(int MealTypeId, int PreferenceId, int FrequencyId, decimal Price, DateTime UpdatedAt, string? UpdatedBy);

/// <summary>The price list grid: rows = meal type × preference, columns = frequencies (active values, in dictionary order).</summary>
public sealed record PriceListDto(IReadOnlyList<PriceOptionDto> MealTypes, IReadOnlyList<PriceOptionDto> Preferences, IReadOnlyList<PriceOptionDto> Frequencies,
    IReadOnlyList<PriceCellDto> Prices);

public sealed class SavePriceCell
{
    public int MealTypeId { get; set; }
    public int PreferenceId { get; set; }
    public int FrequencyId { get; set; }

    /// <summary>Null or empty = not sold (the price is removed).</summary>
    public decimal? Price { get; set; }
}

public sealed class SavePriceListRequest
{
    /// <summary>The cells to change; cells not sent stay as they are.</summary>
    public List<SavePriceCell> Prices { get; set; } = [];
}

/// <summary>Money for a set of delivered boxes. Earned = package price ÷ deliveries per package, for each delivery made.</summary>
/// <param name="FoodBudget">What is left for food after delivery, helper and the target profit (negative = price too low).</param>
public sealed record MoneyLineDto(string Label, int Boxes, decimal Earned, decimal DeliveryCost, decimal HelperCost, decimal TargetProfit, decimal FoodBudget);

/// <param name="UnpricedBoxes">Boxes delivered by packages without a price; they are left out of the money figures.</param>
public sealed record EarningsDto(DateOnly From, DateOnly To, int DeliveryDays, MoneyLineDto Totals, IReadOnlyList<MoneyLineDto> ByBox,
    IReadOnlyList<MoneyLineDto> ByGroup, int UnpricedBoxes);

/// <summary>
/// Money (owner's design with Siva, 2026-11-10; Manager accounts only): costs per box (delivery person + petrol ₹30, helper ₹25), a target
/// profit per frequency as a percentage of the selling price (Monthly 30 %, Weekly 40 %, Trail 40 %), the price list per meal type ×
/// preference × frequency, and what the packages earn per day, week or month. Each package keeps the price it was sold at
/// (<see cref="CustomerPackage.Price"/>). The food budget is not entered: it is what remains of each meal's price after the costs and
/// the target profit. Audit entries record that prices changed but never the amounts, because Admins can read the audit log.
/// </summary>
public sealed class MoneyService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IAuditLogger audit, ICurrentUser currentUser, IClock clock)
{
    public const string DeliveryCostKey = "Money.DeliveryCostPerBox";
    public const string HelperCostKey = "Money.HelperCostPerBox";
    public const string ProfitTargetKeyPrefix = "Money.ProfitTarget.";
    public const decimal DefaultDeliveryCost = 30m;
    public const decimal DefaultHelperCost = 25m;
    public const decimal DefaultProfitTarget = 30m;
    public const decimal MaxCostPerBox = 10_000m;
    public const decimal MaxProfitTarget = 95m;
    public const int MaxEarningsDays = 400;

    /// <summary>Whether the signed-in user may see money (prices, costs, profit): Manager accounts only (owner, 2026-11-10).</summary>
    public static bool CanSeeMoney(ICurrentUser user) => user.Role == Roles.Manager;

    public async Task<MoneySettingsDto> GetSettingsAsync(CancellationToken ct = default)
    {
        var values = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith("Money.")).ToListAsync(ct);
        var frequencies = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.Frequency && e.IsActive)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName).ToListAsync(ct);
        decimal Value(string key, decimal fallback) =>
            decimal.TryParse(values.FirstOrDefault(v => v.Key == key)?.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : fallback;
        var latest = values.OrderByDescending(v => v.UpdatedAt).FirstOrDefault();
        return new MoneySettingsDto(Value(DeliveryCostKey, DefaultDeliveryCost), Value(HelperCostKey, DefaultHelperCost),
            frequencies.Select(f => new ProfitTargetDto(f.Id, f.DisplayName, f.NumericValue, Value(ProfitTargetKeyPrefix + f.Code, DefaultProfitTarget))).ToList(),
            latest?.UpdatedAt, latest?.UpdatedBy);
    }

    public async Task<MoneySettingsDto> SaveSettingsAsync(SaveMoneySettingsRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();
        if (request.DeliveryCostPerBox is < 0 or > MaxCostPerBox) errors.Add($"The delivery cost must be between ₹0 and ₹{MaxCostPerBox:N0} per box.");
        if (request.HelperCostPerBox is < 0 or > MaxCostPerBox) errors.Add($"The helper cost must be between ₹0 and ₹{MaxCostPerBox:N0} per box.");
        if (request.Targets.Any(t => t.Percent is < 0 or > MaxProfitTarget)) errors.Add($"A profit target must be between 0 % and {MaxProfitTarget} %.");
        var frequencies = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.Frequency).ToDictionaryAsync(e => e.Id, ct);
        if (request.Targets.Any(t => !frequencies.ContainsKey(t.FrequencyId))) errors.Add("Unknown frequency.");
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        await SetAsync(DeliveryCostKey, request.DeliveryCostPerBox, ct);
        await SetAsync(HelperCostKey, request.HelperCostPerBox, ct);
        foreach (var t in request.Targets) await SetAsync(ProfitTargetKeyPrefix + frequencies[t.FrequencyId].Code, t.Percent, ct);
        audit.Record(AuditActions.SettingsUpdated, "Settings", "Money", "Money costs and profit targets changed (Money page)", null);
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(ct);
    }

    public async Task<PriceListDto> GetPriceListAsync(CancellationToken ct = default)
    {
        var entries = await db.DictionaryEntries.AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName).ToListAsync(ct);
        List<PriceOptionDto> Of(DictionaryType type) =>
            entries.Where(e => e.DictionaryType == type).Select(e => new PriceOptionDto(e.Id, e.DisplayName, type == DictionaryType.Frequency ? e.NumericValue : null)).ToList();
        var prices = await db.PackagePrices.AsNoTracking()
            .Select(p => new PriceCellDto(p.MealTypeId, p.PreferenceId, p.FrequencyId, p.Price, p.UpdatedAt, p.UpdatedBy)).ToListAsync(ct);
        return new PriceListDto(Of(DictionaryType.MealType), Of(DictionaryType.Preference), Of(DictionaryType.Frequency), prices);
    }

    public async Task<PriceListDto> SavePriceListAsync(SavePriceListRequest request, CancellationToken ct = default)
    {
        var cells = request.Prices ?? [];
        if (cells.GroupBy(c => (c.MealTypeId, c.PreferenceId, c.FrequencyId)).Any(g => g.Count() > 1))
            throw new BusinessRuleException("The same price was sent twice.");
        if (cells.Any(c => c.Price is <= 0 or > PackagePrice.MaxPrice))
            throw new BusinessRuleException($"A price must be more than ₹0 and at most ₹{PackagePrice.MaxPrice:N0}; leave it empty when the box is not sold.");
        var types = await db.DictionaryEntries.AsNoTracking().ToDictionaryAsync(e => e.Id, e => e.DictionaryType, ct);
        if (cells.Any(c => types.GetValueOrDefault(c.MealTypeId) != DictionaryType.MealType || types.GetValueOrDefault(c.PreferenceId) != DictionaryType.Preference
                           || types.GetValueOrDefault(c.FrequencyId) != DictionaryType.Frequency))
            throw new BusinessRuleException("A price refers to an unknown meal type, preference or frequency.");

        var existing = await db.PackagePrices.ToDictionaryAsync(p => (p.MealTypeId, p.PreferenceId, p.FrequencyId), ct);
        var changed = 0;
        foreach (var c in cells)
        {
            var key = (c.MealTypeId, c.PreferenceId, c.FrequencyId);
            var price = c.Price is { } p ? Math.Round(p, 2) : (decimal?)null;
            if (existing.TryGetValue(key, out var row))
            {
                if (price is null) { db.PackagePrices.Remove(row); changed++; }
                else if (row.Price != price) { row.Price = price.Value; changed++; }
            }
            else if (price is not null)
            {
                db.PackagePrices.Add(new PackagePrice { MealTypeId = c.MealTypeId, PreferenceId = c.PreferenceId, FrequencyId = c.FrequencyId, Price = price.Value });
                changed++;
            }
        }
        if (changed > 0)
        {
            audit.Record(AuditActions.SettingsUpdated, "Settings", "PriceList", $"Price list changed on the Money page ({changed} price(s))", null);
            await db.SaveChangesAsync(ct);
        }
        return await GetPriceListAsync(ct);
    }

    /// <summary>The list price of a package: box price × quantity, or null when the box is not in the price list.</summary>
    public static async Task<decimal?> ListPriceAsync(IFitzobitDbContext db, int mealTypeId, int preferenceId, int frequencyId, int quantity,
        CancellationToken ct = default)
    {
        var price = await db.PackagePrices.AsNoTracking()
            .Where(p => p.MealTypeId == mealTypeId && p.PreferenceId == preferenceId && p.FrequencyId == frequencyId)
            .Select(p => (decimal?)p.Price).FirstOrDefaultAsync(ct);
        return price * quantity;
    }

    /// <summary>
    /// What the packages earn from <paramref name="from"/> to <paramref name="to"/> (both included): every delivery day a package delivers
    /// (started, not ended, not paused that day; past packages included) earns its price ÷ its frequency's deliveries and costs delivery +
    /// helper per box. Customer status is not looked at: a delivery that happened (or is planned) counts.
    /// </summary>
    public async Task<EarningsDto> EarningsAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var start = from ?? DeliveryScheduleService.Today(clock);
        var end = to ?? start;
        if (end < start) throw new BusinessRuleException("The end of the period cannot be before its start.");
        if (end.DayNumber - start.DayNumber + 1 > MaxEarningsDays) throw new BusinessRuleException($"Choose a period of at most {MaxEarningsDays} days.");

        var settings = await GetSettingsAsync(ct);
        var allTargets = await db.AppSettings.AsNoTracking().Where(s => s.Key.StartsWith(ProfitTargetKeyPrefix)).ToListAsync(ct);
        var packages = await db.CustomerPackages.AsNoTracking()
            .Where(p => p.StartDate <= end && p.EndDate >= start)
            .Select(p => new
            {
                p.Id, p.CustomerId, p.Quantity, p.Price, p.StartDate, p.EndDate,
                MealType = p.MealType!.DisplayName, MealOrder = p.MealType.SortOrder,
                Preference = p.Preference!.DisplayName, PreferenceOrder = p.Preference.SortOrder,
                FrequencyCode = p.Frequency!.Code, Deliveries = p.Frequency.NumericValue
            })
            .ToListAsync(ct);
        var ids = packages.Select(p => p.Id).ToList();
        var paused = (await db.PackagePauses.AsNoTracking().Where(x => ids.Contains(x.PackageId) && x.PausedDate >= start && x.PausedDate <= end)
            .Select(x => new { x.PackageId, x.PausedDate }).ToListAsync(ct)).ToLookup(x => x.PackageId, x => x.PausedDate);
        var customerIds = packages.Select(p => p.CustomerId).Distinct().ToList();
        var groups = await db.CustomerDeliveryGroupAssignments.AsNoTracking().Where(a => a.IsActive && customerIds.Contains(a.CustomerId))
            .Select(a => new { a.CustomerId, a.DeliveryGroup!.GroupNumber }).ToDictionaryAsync(a => a.CustomerId, a => a.GroupNumber, ct);

        decimal Target(string code) => decimal.TryParse(allTargets.FirstOrDefault(t => t.Key == ProfitTargetKeyPrefix + code)?.Value, NumberStyles.Number,
            CultureInfo.InvariantCulture, out var d) ? d : DefaultProfitTarget;
        var deliveryDays = 0;
        for (var d = start; d <= end; d = d.AddDays(1)) if (calendar.IsDeliveryDay(d)) deliveryDays++;

        var total = new Sum();
        var byBox = new Dictionary<(string, string), (int, int, Sum)>();
        var byGroup = new Dictionary<int, Sum>(); // group number; 0 = no group
        var unpriced = 0;
        foreach (var p in packages)
        {
            var priced = p.Price is not null && p.Deliveries is > 0;
            var perDay = priced ? p.Price!.Value / p.Deliveries!.Value : 0m;
            var target = Target(p.FrequencyCode);
            var pausedDays = paused[p.Id].ToHashSet();
            var first = p.StartDate > start ? p.StartDate : start;
            var last = p.EndDate < end ? p.EndDate : end;
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                if (!calendar.IsDeliveryDay(d) || pausedDays.Contains(d)) continue;
                if (!priced) { unpriced += p.Quantity; continue; }
                var group = groups.TryGetValue(p.CustomerId, out var g) ? g : 0;
                if (!byBox.TryGetValue((p.MealType, p.Preference), out var box)) byBox[(p.MealType, p.Preference)] = box = (p.MealOrder, p.PreferenceOrder, new Sum());
                if (!byGroup.TryGetValue(group, out var groupSum)) byGroup[group] = groupSum = new Sum();
                foreach (var sum in new[] { total, box.Item3, groupSum })
                    sum.Add(p.Quantity, perDay, settings.DeliveryCostPerBox, settings.HelperCostPerBox, target);
            }
        }

        return new EarningsDto(start, end, deliveryDays, total.ToDto("Total"),
            byBox.OrderBy(b => b.Value.Item1).ThenBy(b => b.Key.Item1).ThenBy(b => b.Value.Item2).ThenBy(b => b.Key.Item2)
                .Select(b => b.Value.Item3.ToDto($"{b.Key.Item1} · {b.Key.Item2}")).ToList(),
            byGroup.OrderBy(g => g.Key == 0).ThenBy(g => g.Key).Select(g => g.Value.ToDto(g.Key > 0 ? DeliveryGroup.Label(g.Key) : "No group")).ToList(),
            unpriced);
    }

    /// <summary>How one meal's price splits: price per meal = package price ÷ deliveries; profit = that × target %; food = the rest after the
    /// delivery and helper costs and the profit.</summary>
    public static (decimal PerMeal, decimal Profit, decimal Food) Split(decimal packagePrice, int deliveries, decimal deliveryCost, decimal helperCost,
        decimal targetPercent)
    {
        var perMeal = packagePrice / deliveries;
        var profit = perMeal * targetPercent / 100m;
        return (perMeal, profit, perMeal - deliveryCost - helperCost - profit);
    }

    private sealed class Sum
    {
        private int _boxes;
        private decimal _earned, _delivery, _helper, _profit;

        public void Add(int boxes, decimal earned, decimal deliveryPerBox, decimal helperPerBox, decimal targetPercent)
        {
            _boxes += boxes;
            _earned += earned;
            _delivery += boxes * deliveryPerBox;
            _helper += boxes * helperPerBox;
            _profit += earned * targetPercent / 100m;
        }

        public MoneyLineDto ToDto(string label) => new(label, _boxes, R(_earned), R(_delivery), R(_helper), R(_profit), R(_earned - _delivery - _helper - _profit));

        private static decimal R(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private async Task SetAsync(string key, decimal value, CancellationToken ct)
    {
        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = key });
        setting.Value = value.ToString(CultureInfo.InvariantCulture);
        setting.UpdatedBy = currentUser.UserName;
    }
}
