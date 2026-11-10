using Fitzobit.Application.Money;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

/// <summary>The Money page: costs and profit targets, the price list, and what the packages earn. Manager accounts only.</summary>
[ApiController]
[Route("api/money")]
[Authorize(Policy = Policies.Money)]
public sealed class MoneyController(MoneyService money) : ControllerBase
{
    [HttpGet("settings")]
    public Task<MoneySettingsDto> Settings([FromQuery] DateOnly? date, CancellationToken ct) => money.GetSettingsAsync(date, ct);

    [HttpPut("settings")]
    public Task<MoneySettingsDto> SaveSettings(SaveMoneySettingsRequest request, CancellationToken ct) => money.SaveSettingsAsync(request, ct);

    [HttpGet("prices")]
    public Task<PriceListDto> Prices(CancellationToken ct) => money.GetPriceListAsync(ct);

    [HttpPut("prices")]
    public Task<PriceListDto> SavePrices(SavePriceListRequest request, CancellationToken ct) => money.SavePriceListAsync(request, ct);

    /// <summary>Earnings from <paramref name="from"/> to <paramref name="to"/> (both included); without dates, today.</summary>
    [HttpGet("earnings")]
    public Task<EarningsDto> Earnings([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => money.EarningsAsync(from, to, ct);
}
