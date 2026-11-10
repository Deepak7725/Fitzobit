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
using Fitzobit.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Fitzobit.Application.Money;

/// <param name="Percent">Target profit as a percentage of the selling price (owner: Monthly 30, Weekly 40).</param>
/// <param name="DeliveryDays">Deliveries in a package of this frequency (26, 6, 1 …).</param>
public sealed record ProfitTargetDto(int FrequencyId, string Frequency, int? DeliveryDays, decimal Percent);

/// <summary>A cost line added by the Manager, per box delivered, for all boxes (e.g. "Plate" ₹10).</summary>
public sealed record CostLineDto(string Name, decimal AmountPerBox);

/// <summary>One saved set of costs and targets: valid from <see cref="EffectiveFrom"/> until the next one starts.</summary>
public sealed record CostVersionDto(DateOnly EffectiveFrom, decimal CostsPerBox, DateTime UpdatedAt, string? UpdatedBy);

/// <param name="EffectiveFrom">The day these values started to apply (the version valid on the requested day).</param>
/// <param name="DeliveryCostPerBox">Delivery person + petrol, per box delivered.</param>
/// <param name="HelperCostPerBox">Helper, per box delivered.</param>
/// <param name="History">Every saved version, newest first.</param>
public sealed record MoneySettingsDto(DateOnly EffectiveFrom, decimal DeliveryCostPerBox, decimal HelperCostPerBox, IReadOnlyList<CostLineDto> Lines,
    IReadOnlyList<ProfitTargetDto> Targets, DateTime? UpdatedAt, string? UpdatedBy, IReadOnlyList<CostVersionDto> History);

public sealed class SaveProfitTarget
{
    public int FrequencyId { get; set; }
    public decimal Percent { get; set; }
}

public sealed class SaveCostLine
{
    public string? Name { get; set; }
    public decimal AmountPerBox { get; set; }
}

public sealed class SaveMoneySettingsRequest
{
    /// <summary>First day the values apply (default today). Days before keep the earlier values; a later saved version still wins from its own day.</summary>
    public DateOnly? EffectiveFrom { get; set; }
    public decimal DeliveryCostPerBox { get; set; }
    public decimal HelperCostPerBox { get; set; }
    public List<SaveCostLine> Lines { get; set; } = [];
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
/// <param name="OtherCosts">The Manager's added cost lines by name (amount for these boxes).</param>
/// <param name="FoodBudget">What is left for food after all costs and the target profit (negative = price too low).</param>
/// <param name="Key">By box: "mealTypeId-preferenceId"; by group: the group number (0 = no group).</param>
public sealed record MoneyLineDto(string Label, int Boxes, decimal Earned, decimal DeliveryCost, decimal HelperCost, IReadOnlyDictionary<string, decimal> OtherCosts,
    decimal TargetProfit, decimal FoodBudget, string? Key = null);

/// <param name="Filled">Packages that got their list price × quantity.</param>
/// <param name="StillMissing">Packages still without a price because their box has no price in the price list.</param>
public sealed record FillPricesResultDto(int Filled, int StillMissing);

/// <summary>A package without a price that delivered boxes in the period (so the Manager can give it one).</summary>
public sealed record UnpricedPackageDto(int CustomerId, string CustomerCode, string CustomerName, string Package, int Boxes);

/// <param name="CostNames">Names of the added cost lines that occur in the period, in display order.</param>
/// <param name="UnpricedBoxes">Boxes delivered by packages without a price; they are left out of the money figures.</param>
/// <param name="UnpricedPackages">Those packages, by customer.</param>
public sealed record EarningsDto(DateOnly From, DateOnly To, int DeliveryDays, IReadOnlyList<string> CostNames, MoneyLineDto Totals,
    IReadOnlyList<MoneyLineDto> ByBox, IReadOnlyList<MoneyLineDto> ByGroup, int UnpricedBoxes, IReadOnlyList<UnpricedPackageDto> UnpricedPackages);

/// <summary>
/// Money (owner's design with Siva, 2026-11-10; Manager accounts only): costs per box (delivery person + petrol ₹30, helper ₹25, plus
/// cost lines the Manager adds by name, e.g. Plate ₹10; all kept with history by "valid from" date), a target
/// profit per frequency as a percentage of the selling price (Monthly 30 %, Weekly 40 %, Trail 40 %), the price list per meal type ×
/// preference × frequency, and what the packages earn per day, week or month. Each package keeps the price it was sold at
/// (<see cref="CustomerPackage.Price"/>). The food budget is not entered: it is what remains of each meal's price after the costs and
/// the target profit. Audit entries record that prices changed but never the amounts, because Admins can read the audit log.
/// </summary>
public sealed class MoneyService(IFitzobitDbContext db, IDeliveryCalendarService calendar, IAuditLogger audit, ICurrentUser currentUser, IClock clock)
{
    public const decimal DefaultDeliveryCost = 30m;
    public const decimal DefaultHelperCost = 25m;
    public const decimal DefaultProfitTarget = 30m;
    public const int MaxCostLines = 30;
    public const decimal MaxCostPerBox = MoneyCostVersion.MaxCostPerBox;
    public const decimal MaxProfitTarget = MoneyProfitTarget.MaxPercent;
    public const int MaxEarningsDays = 400;

    /// <summary>Whether the signed-in user may see money (prices, costs, profit): Manager accounts only (owner, 2026-11-10).</summary>
    public static bool CanSeeMoney(ICurrentUser user) => user.Role == Roles.Manager;

    /// <summary>The costs and targets valid on <paramref name="date"/> (default today), plus the list of saved versions.</summary>
    public async Task<MoneySettingsDto> GetSettingsAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? DeliveryScheduleService.Today(clock);
        var versions = await LoadVersionsAsync(ct);
        var frequencies = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.Frequency && e.IsActive)
            .OrderBy(e => e.SortOrder).ThenBy(e => e.DisplayName).ToListAsync(ct);
        var v = VersionOn(versions, day);
        var history = versions.OrderByDescending(x => x.EffectiveFrom)
            .Select(x => new CostVersionDto(x.EffectiveFrom, x.DeliveryCostPerBox + x.HelperCostPerBox + x.Lines.Sum(l => l.AmountPerBox), x.UpdatedAt, x.UpdatedBy))
            .ToList();
        return new MoneySettingsDto(v?.EffectiveFrom ?? day, v?.DeliveryCostPerBox ?? DefaultDeliveryCost, v?.HelperCostPerBox ?? DefaultHelperCost,
            v?.Lines.OrderBy(l => l.SortOrder).Select(l => new CostLineDto(l.Name, l.AmountPerBox)).ToList() ?? [],
            frequencies.Select(f => new ProfitTargetDto(f.Id, f.DisplayName, f.NumericValue, TargetOf(v, f.Id))).ToList(),
            v?.UpdatedAt, v?.UpdatedBy, history);
    }

    /// <summary>
    /// Saves costs and targets valid from <see cref="SaveMoneySettingsRequest.EffectiveFrom"/> (default today): a new version, or the
    /// version of that same day replaced. Days before keep their values (owner, 2026-11-10: keep cost history).
    /// </summary>
    public async Task<MoneySettingsDto> SaveSettingsAsync(SaveMoneySettingsRequest request, CancellationToken ct = default)
    {
        var from = request.EffectiveFrom ?? DeliveryScheduleService.Today(clock);
        var lines = (request.Lines ?? []).Select(l => (Name: (l.Name ?? "").Trim(), l.AmountPerBox)).ToList();
        var targets = request.Targets ?? [];
        var errors = new List<string>();
        if (request.DeliveryCostPerBox is < 0 or > MaxCostPerBox) errors.Add($"The delivery cost must be between ₹0 and ₹{MaxCostPerBox:N0} per box.");
        if (request.HelperCostPerBox is < 0 or > MaxCostPerBox) errors.Add($"The helper cost must be between ₹0 and ₹{MaxCostPerBox:N0} per box.");
        if (lines.Any(l => l.Name.Length == 0)) errors.Add("Every cost line needs a name.");
        if (lines.Any(l => l.Name.Length > MoneyCostLine.MaxNameLength)) errors.Add($"A cost name can be at most {MoneyCostLine.MaxNameLength} characters.");
        if (lines.Any(l => l.AmountPerBox is < 0 or > MaxCostPerBox)) errors.Add($"A cost must be between ₹0 and ₹{MaxCostPerBox:N0} per box.");
        if (lines.GroupBy(l => l.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) errors.Add("Two cost lines have the same name.");
        if (lines.Count > MaxCostLines) errors.Add($"At most {MaxCostLines} cost lines.");
        if (targets.Any(t => t.Percent is < 0 or > MaxProfitTarget)) errors.Add($"A profit target must be between 0 % and {MaxProfitTarget} %.");
        if (targets.GroupBy(t => t.FrequencyId).Any(g => g.Count() > 1)) errors.Add("A frequency's target was sent twice.");
        var frequencyIds = await db.DictionaryEntries.AsNoTracking().Where(e => e.DictionaryType == DictionaryType.Frequency).Select(e => e.Id).ToListAsync(ct);
        if (targets.Any(t => !frequencyIds.Contains(t.FrequencyId))) errors.Add("Unknown frequency.");
        if (errors.Count > 0) throw new BusinessRuleException(string.Join(" ", errors));

        var previous = VersionOn(await LoadVersionsAsync(ct), from.AddDays(-1));
        var version = await db.MoneyCostVersions.Include(v => v.Lines).Include(v => v.Targets).FirstOrDefaultAsync(v => v.EffectiveFrom == from, ct);
        if (version is null) db.MoneyCostVersions.Add(version = new MoneyCostVersion { EffectiveFrom = from });
        version.DeliveryCostPerBox = Math.Round(request.DeliveryCostPerBox, 2);
        version.HelperCostPerBox = Math.Round(request.HelperCostPerBox, 2);
        foreach (var old in version.Lines.ToList()) version.Lines.Remove(old);
        for (var i = 0; i < lines.Count; i++)
            version.Lines.Add(new MoneyCostLine { Name = lines[i].Name, AmountPerBox = Math.Round(lines[i].AmountPerBox, 2), SortOrder = i });
        foreach (var old in version.Targets.ToList()) version.Targets.Remove(old);
        // Frequencies not sent keep the target they had before this day.
        foreach (var id in frequencyIds)
        {
            var sent = targets.FirstOrDefault(t => t.FrequencyId == id);
            var percent = sent?.Percent ?? previous?.Targets.FirstOrDefault(t => t.FrequencyId == id)?.Percent;
            if (percent is { } p) version.Targets.Add(new MoneyProfitTarget { FrequencyId = id, Percent = Math.Round(p, 2) });
        }
        audit.Record(AuditActions.SettingsUpdated, "Settings", "Money", $"Money costs and profit targets changed on the Money page (valid from {from:dd MMM yyyy})", null);
        await db.SaveChangesAsync(ct);
        return await GetSettingsAsync(from, ct);
    }

    private async Task<List<MoneyCostVersion>> LoadVersionsAsync(CancellationToken ct) =>
        await db.MoneyCostVersions.AsNoTracking().Include(v => v.Lines).Include(v => v.Targets).OrderBy(v => v.EffectiveFrom).ToListAsync(ct);

    /// <summary>The version valid on <paramref name="day"/>: the latest that started on or before it; before the first one, the first one.</summary>
    private static MoneyCostVersion? VersionOn(IReadOnlyList<MoneyCostVersion> ordered, DateOnly day) =>
        ordered.LastOrDefault(v => v.EffectiveFrom <= day) ?? ordered.FirstOrDefault();

    private static decimal TargetOf(MoneyCostVersion? version, int frequencyId) =>
        version?.Targets.FirstOrDefault(t => t.FrequencyId == frequencyId)?.Percent ?? DefaultProfitTarget;

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

    /// <summary>
    /// Gives every package without a price — current and past (finished packages cannot be edited) — its list price × quantity, where
    /// the price list has a price for its box. Packages that already have a price are never changed (owner, 2026-11-10).
    /// </summary>
    public async Task<FillPricesResultDto> FillMissingPricesAsync(CancellationToken ct = default)
    {
        var prices = await db.PackagePrices.AsNoTracking()
            .ToDictionaryAsync(p => (p.MealTypeId, p.PreferenceId, p.FrequencyId), p => p.Price, ct);
        var packages = await db.CustomerPackages.Where(p => p.Price == null).ToListAsync(ct);
        var filled = 0;
        foreach (var p in packages)
        {
            if (!prices.TryGetValue((p.MealTypeId, p.PreferenceId, p.FrequencyId), out var price)) continue;
            p.Price = price * p.Quantity;
            filled++;
        }
        if (filled > 0)
        {
            audit.Record(AuditActions.SettingsUpdated, "Settings", "PriceList",
                $"Missing package prices filled from the price list on the Money page ({filled} package(s))", null);
            await db.SaveChangesAsync(ct);
        }
        return new FillPricesResultDto(filled, packages.Count - filled);
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

        var versions = await LoadVersionsAsync(ct);
        var packages = await db.CustomerPackages.AsNoTracking()
            .Where(p => p.StartDate <= end && p.EndDate >= start)
            .Select(p => new
            {
                p.Id, p.CustomerId, p.Quantity, p.Price, p.StartDate, p.EndDate,
                MealType = p.MealType!.DisplayName, MealOrder = p.MealType.SortOrder,
                Preference = p.Preference!.DisplayName, PreferenceOrder = p.Preference.SortOrder,
                p.MealTypeId, p.PreferenceId, p.FrequencyId, Deliveries = p.Frequency!.NumericValue, Frequency = p.Frequency.DisplayName,
                CustomerCode = p.Customer!.CustomerId, p.Customer.CustomerName
            })
            .ToListAsync(ct);
        var ids = packages.Select(p => p.Id).ToList();
        var paused = (await db.PackagePauses.AsNoTracking().Where(x => ids.Contains(x.PackageId) && x.PausedDate >= start && x.PausedDate <= end)
            .Select(x => new { x.PackageId, x.PausedDate }).ToListAsync(ct)).ToLookup(x => x.PackageId, x => x.PausedDate);
        var customerIds = packages.Select(p => p.CustomerId).Distinct().ToList();
        var groups = await db.CustomerDeliveryGroupAssignments.AsNoTracking().Where(a => a.IsActive && customerIds.Contains(a.CustomerId))
            .Select(a => new { a.CustomerId, a.DeliveryGroup!.GroupNumber }).ToDictionaryAsync(a => a.CustomerId, a => a.GroupNumber, ct);

        var deliveryDays = 0;
        for (var d = start; d <= end; d = d.AddDays(1)) if (calendar.IsDeliveryDay(d)) deliveryDays++;

        var total = new Sum();
        var byBox = new Dictionary<(string, string), (int, int, string, Sum)>();
        var costNames = new List<string>();
        foreach (var v in versions.OrderByDescending(v => v.EffectiveFrom))
            foreach (var l in v.Lines.OrderBy(l => l.SortOrder))
                if (!costNames.Contains(l.Name, StringComparer.OrdinalIgnoreCase)) costNames.Add(l.Name);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byGroup = new Dictionary<int, Sum>(); // group number; 0 = no group
        var unpriced = 0;
        var unpricedPackages = new List<UnpricedPackageDto>();
        foreach (var p in packages)
        {
            var priced = p.Price is not null && p.Deliveries is > 0;
            var perDay = priced ? p.Price!.Value / p.Deliveries!.Value : 0m;
            var pausedDays = paused[p.Id].ToHashSet();
            var unpricedBefore = unpriced;
            var first = p.StartDate > start ? p.StartDate : start;
            var last = p.EndDate < end ? p.EndDate : end;
            for (var d = first; d <= last; d = d.AddDays(1))
            {
                if (!calendar.IsDeliveryDay(d) || pausedDays.Contains(d)) continue;
                if (!priced) { unpriced += p.Quantity; continue; }
                var group = groups.TryGetValue(p.CustomerId, out var g) ? g : 0;
                var version = VersionOn(versions, d); // the costs valid on that day
                if (!byBox.TryGetValue((p.MealType, p.Preference), out var box))
                    byBox[(p.MealType, p.Preference)] = box = (p.MealOrder, p.PreferenceOrder, $"{p.MealTypeId}-{p.PreferenceId}", new Sum());
                if (!byGroup.TryGetValue(group, out var groupSum)) byGroup[group] = groupSum = new Sum();
                var lines = version?.Lines.ToList() ?? [];
                foreach (var l in lines) usedNames.Add(l.Name);
                foreach (var sum in new[] { total, box.Item4, groupSum })
                    sum.Add(p.Quantity, perDay, version?.DeliveryCostPerBox ?? DefaultDeliveryCost, version?.HelperCostPerBox ?? DefaultHelperCost,
                        lines, TargetOf(version, p.FrequencyId));
            }
            if (unpriced > unpricedBefore)
                unpricedPackages.Add(new UnpricedPackageDto(p.CustomerId, p.CustomerCode, p.CustomerName,
                    $"{p.Preference} {p.MealType} ×{p.Quantity} · {p.Frequency}", unpriced - unpricedBefore));
        }

        return new EarningsDto(start, end, deliveryDays, costNames.Where(usedNames.Contains).ToList(), total.ToDto("Total", null),
            byBox.OrderBy(b => b.Value.Item1).ThenBy(b => b.Key.Item1).ThenBy(b => b.Value.Item2).ThenBy(b => b.Key.Item2)
                .Select(b => b.Value.Item4.ToDto($"{b.Key.Item1} · {b.Key.Item2}", b.Value.Item3)).ToList(),
            byGroup.OrderBy(g => g.Key == 0).ThenBy(g => g.Key)
                .Select(g => g.Value.ToDto(g.Key > 0 ? DeliveryGroup.Label(g.Key) : "No group", g.Key.ToString(CultureInfo.InvariantCulture))).ToList(),
            unpriced, unpricedPackages.OrderBy(u => u.CustomerCode).ToList());
    }

    /// <summary>How one meal's price splits: price per meal = package price ÷ deliveries; profit = that × target %; food = the rest after the
    /// per-box costs (delivery, helper and the added lines) and the profit.</summary>
    public static (decimal PerMeal, decimal Profit, decimal Food) Split(decimal packagePrice, int deliveries, decimal costsPerBox, decimal targetPercent)
    {
        var perMeal = packagePrice / deliveries;
        var profit = perMeal * targetPercent / 100m;
        return (perMeal, profit, perMeal - costsPerBox - profit);
    }

    private sealed class Sum
    {
        private int _boxes;
        private decimal _earned, _delivery, _helper, _profit;
        private readonly Dictionary<string, decimal> _other = new(StringComparer.OrdinalIgnoreCase);

        public void Add(int boxes, decimal earned, decimal deliveryPerBox, decimal helperPerBox, IEnumerable<MoneyCostLine> lines, decimal targetPercent)
        {
            _boxes += boxes;
            _earned += earned;
            _delivery += boxes * deliveryPerBox;
            _helper += boxes * helperPerBox;
            foreach (var l in lines) _other[l.Name] = _other.GetValueOrDefault(l.Name) + boxes * l.AmountPerBox;
            _profit += earned * targetPercent / 100m;
        }

        public MoneyLineDto ToDto(string label, string? key) =>
            new(label, _boxes, R(_earned), R(_delivery), R(_helper), _other.ToDictionary(o => o.Key, o => R(o.Value)), R(_profit),
                R(_earned - _delivery - _helper - _other.Values.Sum() - _profit), key);

        private static decimal R(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    }
}
