using System.Net;
using Fitzobit.Application.Users;
using Fitzobit.Domain.Users;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Fitzobit.Web.Controllers;

public sealed record SignedInUserDto(int Id, string UserName, string DisplayName, string Role);
public sealed record AuthStateDto(bool SetupRequired, bool SetupAllowedHere, bool Authenticated, SignedInUserDto? User);
public sealed record LoginRequest(string UserName, string Password);
public sealed record SetupRequest(string UserName, string DisplayName, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>
/// Sign-in, sign-out, first-run administrator setup and the CSRF token. After login or setup the client calls
/// <c>GET state</c> again: the CSRF token is bound to the signed-in user.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth, UserService users, IAntiforgery antiforgery, IConfiguration configuration) : ControllerBase
{
    public const string CsrfCookieName = "XSRF-TOKEN";

    [HttpGet("state")]
    [AllowAnonymous]
    public async Task<AuthStateDto> State(CancellationToken ct)
    {
        IssueCsrfCookie();
        var setupRequired = await auth.SetupRequiredAsync(ct);
        var signedIn = User.Identity?.IsAuthenticated == true && User.UserId() is not null;
        return new AuthStateDto(
            setupRequired,
            setupRequired && SetupAllowedFromHere(),
            signedIn,
            signedIn ? new SignedInUserDto(User.UserId()!.Value, User.Identity!.Name!, CurrentDisplayName(), CurrentRole()) : null);
    }

    /// <summary>Creates the first administrator. Only while no user exists, and only from this machine unless Auth:AllowRemoteSetup is set.</summary>
    [HttpPost("setup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Setup(SetupRequest request, CancellationToken ct)
    {
        if (!SetupAllowedFromHere())
            return Problem(statusCode: StatusCodes.Status403Forbidden, title: "Setup not allowed",
                detail: "First-run setup must be completed on the server itself.");
        var user = await auth.CreateFirstAdminAsync(request.UserName, request.DisplayName, request.Password, ct);
        await SignInAsync(user);
        return Ok(ToDto(user));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await auth.SignInAsync(request.UserName, request.Password, ct);
        if (!result.Succeeded)
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Sign-in failed", detail: result.Error);
        await SignInAsync(result.User!);
        return Ok(ToDto(result.User!));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    /// <summary>Changes the signed-in user's password. Other sessions are signed out; this one is re-issued.</summary>
    [HttpPost("change-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.ChangeOwnPasswordAsync(User.UserId()!.Value, request.CurrentPassword, request.NewPassword, ct);
        await SignInAsync(user);
        return NoContent();
    }

    private Task SignInAsync(AppUser user) =>
        HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            FitzobitClaims.For(user, CookieAuthenticationDefaults.AuthenticationScheme),
            new AuthenticationProperties { IsPersistent = false });

    /// <summary>The request token in a JS-readable cookie; the SPA echoes it in the X-XSRF-TOKEN header.</summary>
    private void IssueCsrfCookie()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append(CsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = Request.IsHttps,
            Path = "/"
        });
    }

    private bool SetupAllowedFromHere() =>
        configuration.GetValue("Auth:AllowRemoteSetup", false)
        || HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip);

    private string CurrentDisplayName() => User.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value ?? User.Identity!.Name!;
    private string CurrentRole() => User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";

    private static SignedInUserDto ToDto(AppUser user) => new(user.Id, user.UserName, user.DisplayName, user.Role.ToString());
}
