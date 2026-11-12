using Fitzobit.Application.Customers;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fitzobit.Web.Controllers;

/// <summary>Pause for everyone (festive or emergency days): Managers add and remove them, Admins only see the list.</summary>
[ApiController]
[Route("api/festive-pauses")]
[Authorize(Policy = Policies.Operate)]
public sealed class FestivePausesController(FestivePauseService festive) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FestivePauseDto>> List(CancellationToken ct) => festive.ListAsync(ct);

    /// <summary>What pausing these days for everyone would do, without saving.</summary>
    [HttpPost("preview")]
    [Authorize(Policy = Policies.Manage)]
    public Task<FestivePreviewDto> Preview(FestivePauseRequest request, CancellationToken ct) => festive.PreviewAsync(request, ct);

    [HttpPost]
    [Authorize(Policy = Policies.Manage)]
    public Task<FestivePreviewDto> Create(FestivePauseRequest request, CancellationToken ct) => festive.CreateAsync(request, ct);

    /// <summary>Removes a festive pause before its day; the end dates move back.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.Manage)]
    public async Task<IActionResult> Remove(int id, CancellationToken ct)
    {
        await festive.RemoveAsync(id, ct);
        return NoContent();
    }
}
