using System.Security.Claims;
using Fitzobit.Application.Abstractions;
using Fitzobit.Domain.Users;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fitzobit.Web.Infrastructure;

public static class Policies
{
    /// <summary>Day-to-day operations: Admin or Manager.</summary>
    public const string Operate = "Operate";

    /// <summary>Users, administrative settings, audit log.</summary>
    public const string Admin = "Admin";

    /// <summary>The Money page (prices, costs, profit): Manager accounts only, not Admins (owner, 2026-11-10).</summary>
    public const string Money = "Money";
}

public static class FitzobitClaims
{
    public const string SecurityStamp = "fitzobit:stamp";

    public static ClaimsPrincipal For(AppUser user, string scheme) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(ClaimTypes.GivenName, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(SecurityStamp, user.SecurityStamp)
        ], scheme, ClaimTypes.Name, ClaimTypes.Role));

    public static int? UserId(this ClaimsPrincipal principal) =>
        int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

/// <summary>The signed-in user, from the authentication cookie. Background work (startup seeding) runs as "system".</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;
    private bool IsSignedIn => Principal?.Identity?.IsAuthenticated == true;

    public string UserName => IsSignedIn ? Principal!.Identity!.Name! : accessor.HttpContext is null ? "system" : "anonymous";
    public string DisplayName => IsSignedIn ? Principal!.FindFirstValue(ClaimTypes.GivenName) ?? UserName : UserName;
    public string Role => IsSignedIn ? Principal!.FindFirstValue(ClaimTypes.Role) ?? "" : "";
}

/// <summary>
/// CSRF protection for the JSON API: every state-changing request (anything but GET/HEAD/OPTIONS/TRACE) must carry the
/// antiforgery token in the X-XSRF-TOKEN header; otherwise 400. (MVC's AutoValidateAntiforgeryToken needs the Razor view services.)
/// </summary>
public sealed class ApiAntiforgeryFilter(IAntiforgery antiforgery, ILogger<ApiAntiforgeryFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method)) return;

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            logger.LogWarning("CSRF validation failed for {Method} {Path}: {Reason}", method, context.HttpContext.Request.Path, ex.Message);
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Request could not be verified",
                Detail = "Your session token is missing or has expired. Reload the page and try again."
            }) { StatusCode = StatusCodes.Status400BadRequest };
        }
    }
}

/// <summary>Standard protective response headers for every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; " +
        "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["X-Frame-Options"] = "DENY";
            h["Referrer-Policy"] = "no-referrer";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            h["Cross-Origin-Opener-Policy"] = "same-origin";
            // Swagger UI (development only) needs inline scripts.
            if (!context.Request.Path.StartsWithSegments("/swagger")) h["Content-Security-Policy"] = ContentSecurityPolicy;
            if (context.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
