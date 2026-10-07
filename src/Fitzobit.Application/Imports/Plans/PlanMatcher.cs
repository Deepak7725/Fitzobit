using System.Text.RegularExpressions;
using Fitzobit.Domain.Imports;

namespace Fitzobit.Application.Imports.Plans;

/// <summary>Turns plan text into comparable tokens: lower-case words, common spelling variants unified, filler words removed.</summary>
public static partial class PlanTextNormalizer
{
    private static readonly HashSet<string> StopWords =
        ["and", "the", "of", "plan", "plans", "item", "items", "with", "for", "a", "an", "per", "rs", "inr"];

    private static readonly Dictionary<string, string[]> Synonyms = new()
    {
        ["month"] = ["monthly"], ["months"] = ["monthly"],
        ["week"] = ["weekly"], ["weeks"] = ["weekly"],
        ["day"] = ["days"],
        ["fitbox"] = ["fit", "box"], ["boxes"] = ["box"],
        ["salad"] = ["salads"],
        ["eggs"] = ["egg"],
        ["vegetarian"] = ["veg"],
        ["meal"] = ["meals"],
    };

    public static IReadOnlyList<string> Tokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var spaced = DigitLetterBoundary().Replace(text.ToLowerInvariant().Replace("&", " and "), " ");
        var result = new List<string>();
        foreach (var word in NonAlphanumeric().Split(spaced))
        {
            if (word.Length == 0 || StopWords.Contains(word)) continue;
            if (Synonyms.TryGetValue(word, out var replacements)) result.AddRange(replacements);
            else result.Add(word);
        }
        return result;
    }

    /// <summary>Order-insensitive key: distinct tokens, sorted. "Veg Medium Box" and "medium box (veg)" share a key.</summary>
    public static string Key(string? text) => string.Join(' ', Tokens(text).Distinct().Order(StringComparer.Ordinal));

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"(?<=\d)(?=[a-z])|(?<=[a-z])(?=\d)")]
    private static partial Regex DigitLetterBoundary();
}

public sealed record PlanTierInfo(int TierId, string TierName, string BillingPeriod);

/// <summary>Everything the matcher may use to recognise a plan. Built from the database, never hard-coded.</summary>
public sealed record PlanInfo(
    int PlanId,
    string Name,
    string Code,
    string? Category,
    string? Term,
    string? BillingPeriod,
    string? DurationText,
    IReadOnlyList<PlanTierInfo> Tiers);

public sealed record SavedPlanAlias(string AliasKey, int PlanId, int? PricingTierId);

public sealed record PlanMatchResult(
    PlanMappingStatus Status,
    PlanMatchMethod Method,
    int? PlanId,
    int? PricingTierId,
    IReadOnlyList<int> CandidatePlanIds,
    string? Note);

/// <summary>
/// Deterministic plan recognition, in order: saved alias → exact plan name/code → keywords.
/// A keyword match is accepted only when one plan explains strictly more of the text than any other;
/// ties are reported as ambiguous and left for the manager to decide.
/// </summary>
public sealed class PlanMatcher
{
    private readonly IReadOnlyList<PlanInfo> _plans;
    private readonly Dictionary<string, SavedPlanAlias> _aliases;
    private readonly Dictionary<string, int> _exact = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HashSet<string>> _planTokens = new();
    private readonly Dictionary<int, HashSet<string>> _tierTokens = new();

    public PlanMatcher(IReadOnlyList<PlanInfo> plans, IEnumerable<SavedPlanAlias> aliases)
    {
        _plans = plans;
        _aliases = aliases.GroupBy(a => a.AliasKey).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var plan in plans)
        {
            foreach (var key in new[] { PlanTextNormalizer.Key(plan.Name), PlanTextNormalizer.Key(plan.Code) })
                if (key.Length > 0) _exact.TryAdd(key, plan.PlanId);

            _planTokens[plan.PlanId] = new HashSet<string>(
                new[] { plan.Name, plan.Code, plan.Category, plan.Term, plan.BillingPeriod, plan.DurationText }
                    .SelectMany(PlanTextNormalizer.Tokens));

            foreach (var tier in plan.Tiers)
                _tierTokens[tier.TierId] = new HashSet<string>(
                    PlanTextNormalizer.Tokens(tier.TierName).Concat(PlanTextNormalizer.Tokens(tier.BillingPeriod)));
        }
    }

    public PlanMatchResult Match(string? text)
    {
        var key = PlanTextNormalizer.Key(text);
        if (key.Length == 0)
            return new(PlanMappingStatus.Unknown, PlanMatchMethod.None, null, null, [], "No plan text");

        if (_aliases.TryGetValue(key, out var alias) && _plans.Any(p => p.PlanId == alias.PlanId))
            return new(PlanMappingStatus.Mapped, PlanMatchMethod.SavedAlias, alias.PlanId, alias.PricingTierId, [], null);

        if (_exact.TryGetValue(key, out var exactPlanId))
            return new(PlanMappingStatus.Mapped, PlanMatchMethod.ExactName, exactPlanId, null, [], null);

        var input = PlanTextNormalizer.Tokens(text).ToHashSet();
        var candidates = new List<(PlanInfo Plan, int Coverage)>();
        foreach (var plan in _plans)
        {
            var planTokens = _planTokens[plan.PlanId];
            var explainable = new HashSet<string>(planTokens);
            foreach (var tier in plan.Tiers) explainable.UnionWith(_tierTokens[tier.TierId]);
            if (input.IsSubsetOf(explainable)) candidates.Add((plan, input.Count(planTokens.Contains)));
        }

        if (candidates.Count == 0)
            return new(PlanMappingStatus.Unknown, PlanMatchMethod.None, null, null, [], "No Fitzobit plan matches this text");

        var best = candidates.Max(c => c.Coverage);
        var top = candidates.Where(c => c.Coverage == best).Select(c => c.Plan).ToList();
        if (best == 0 || top.Count > 1)
        {
            var ids = (best == 0 ? candidates.Select(c => c.Plan) : top).Select(p => p.PlanId).ToList();
            return new(PlanMappingStatus.Ambiguous, PlanMatchMethod.None, null, null, ids,
                $"Matches {ids.Count} plans equally well");
        }

        var match = top[0];
        var (tierId, note) = MatchTier(match, input);
        return new(PlanMappingStatus.Mapped, PlanMatchMethod.Keywords, match.PlanId, tierId, [], note);
    }

    /// <summary>
    /// Picks the tier only when the words left over after the plan name (e.g. "veg") identify exactly one tier,
    /// and that tier's own distinguishing words all appear in the text.
    /// </summary>
    private (int? TierId, string? Note) MatchTier(PlanInfo plan, HashSet<string> input)
    {
        var planTokens = _planTokens[plan.PlanId];
        var residual = input.Where(t => !planTokens.Contains(t)).ToHashSet();
        if (residual.Count == 0) return (null, null);

        var matches = plan.Tiers.Where(t =>
        {
            var tokens = _tierTokens[t.TierId];
            return residual.IsSubsetOf(tokens) && tokens.Where(x => !planTokens.Contains(x)).All(input.Contains);
        }).ToList();

        return matches.Count == 1
            ? (matches[0].TierId, null)
            : (null, $"Variant could not be determined from \"{string.Join(' ', residual)}\"");
    }
}
