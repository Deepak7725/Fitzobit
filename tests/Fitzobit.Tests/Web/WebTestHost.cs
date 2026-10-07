using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Fitzobit.Tests.Web;

/// <summary>
/// The real web host (production settings: secure cookies, HSTS, no Swagger) on its own temporary SQLite file.
/// The catalogue is seeded from the real workbook on startup, exactly as in the running app.
/// </summary>
public sealed class WebTestHost : WebApplicationFactory<Program>
{
    private readonly string _databaseFile = Path.Combine(Path.GetTempPath(), $"fitzobit-web-test-{Guid.NewGuid():N}.db");
    private readonly bool _allowRemoteSetup;

    public WebTestHost(bool allowRemoteSetup = true) => _allowRemoteSetup = allowRemoteSetup;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Database:ConnectionString", $"Data Source={_databaseFile}");
        builder.UseSetting("CatalogueSeed:FilePath", TestPaths.CatalogueWorkbook);
        builder.UseSetting("Auth:AllowRemoteSetup", _allowRemoteSetup ? "true" : "false");
        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ThrowingTestController).Assembly));
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
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _databaseFile, _databaseFile + "-wal", _databaseFile + "-shm" })
            try { File.Delete(file); } catch (IOException) { }
    }
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
    public IActionResult Get() => throw new InvalidOperationException("Secret internal detail: Data Source=C:\\private\\fitzobit.db");
}

public static class HttpAssert
{
    public static void Status(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(response.StatusCode == expected, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: expected {(int)expected}, got {(int)response.StatusCode}");
}
