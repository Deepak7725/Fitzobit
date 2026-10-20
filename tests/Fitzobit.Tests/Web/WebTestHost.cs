using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Fitzobit.Tests.Web;

/// <summary>
/// The real web host (production settings: secure cookies, HSTS, no Swagger) on its own temporary PostgreSQL database,
/// migrated on startup exactly as in the running app.
/// </summary>
public sealed class WebTestHost : WebApplicationFactory<Program>
{
    private readonly PostgresTestDatabase _postgres = new();
    private readonly bool _allowRemoteSetup;
    private readonly bool _loopbackClient;
    private readonly string? _setupCode;

    /// <param name="loopbackClient">Requests arrive from 127.0.0.1, as from a browser or a tunnel on the same machine.</param>
    public WebTestHost(bool allowRemoteSetup = true, bool loopbackClient = false, string? setupCode = null)
    {
        _allowRemoteSetup = allowRemoteSetup;
        _loopbackClient = loopbackClient;
        _setupCode = setupCode;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Database:ConnectionString", _postgres.ConnectionString);
        builder.UseSetting("Geocoding:Provider", "None");        // never call a real geocoder from tests
        builder.UseSetting("Geocoding:BackgroundEnabled", "false");
        builder.UseSetting("Automation:Enabled", "false");
        builder.UseSetting("Auth:AllowRemoteSetup", _allowRemoteSetup ? "true" : "false");
        if (_setupCode is not null) builder.UseSetting("Auth:SetupCode", _setupCode);
        builder.ConfigureTestServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(ThrowingTestController).Assembly);
            if (_loopbackClient) services.AddTransient<IStartupFilter, LoopbackClientStartupFilter>();
        });
    }

    public ApiClient NewClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false }));

    /// <summary>A signed-in administrator (first-run setup) with a valid CSRF token.</summary>
    public async Task<ApiClient> SetupAdminAsync()
    {
        var client = NewClient();
        await client.RefreshAsync();
        (await client.PostAsync("/api/auth/setup", new { userName = "owner", displayName = "Fitzobit Owner", password = ApiClient.AdminPassword }))
            .EnsureSuccessStatusCode();
        await client.RefreshAsync();
        return client;
    }

    public async Task<ApiClient> SignInAsync(string userName, string password)
    {
        var client = NewClient();
        await client.RefreshAsync();
        (await client.PostAsync("/api/auth/login", new { userName, password })).EnsureSuccessStatusCode();
        await client.RefreshAsync();
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _postgres.Dispose();
    }
}

/// <summary>Gives every test request a loopback remote address (TestServer leaves it empty).</summary>
internal sealed class LoopbackClientStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            return nextMiddleware(context);
        });
        next(app);
    };
}

/// <summary>HttpClient wrapper that behaves like the SPA: keeps the cookies and echoes the CSRF token header.</summary>
public sealed class ApiClient(HttpClient http)
{
    public const string AdminPassword = "Setup-Pass-2026";
    public HttpClient Http { get; } = http;

    /// <summary>Calls GET /api/auth/state and stores the CSRF token it issues (as the SPA does after sign-in).</summary>
    public async Task<JsonElement> RefreshAsync()
    {
        var response = await Http.GetAsync("/api/auth/state");
        response.EnsureSuccessStatusCode();
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN="));
        var token = Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..cookie.IndexOf(';')]);
        Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);
    public Task<HttpResponseMessage> PostAsync(string url, object body) => Http.PostAsJsonAsync(url, body);
    public Task<HttpResponseMessage> PutAsync(string url, object body) => Http.PutAsJsonAsync(url, body);
    public Task<HttpResponseMessage> DeleteAsync(string url) => Http.DeleteAsync(url);

    public static async Task<string?> DetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString();
}

/// <summary>Test-only endpoint that fails unexpectedly, to check that internals never reach the client.</summary>
[ApiController]
[Route("api/test-only/throw")]
[AllowAnonymous]
public sealed class ThrowingTestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => throw new InvalidOperationException("Secret internal detail: Host=db.internal;Password=secret-password");
}

public static class HttpAssert
{
    public static void Status(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {(int)expected}, got {(int)response.StatusCode}");
}
