using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Fitzobit.Application;
using Fitzobit.Application.Abstractions;
using Fitzobit.Application.Users;
using Fitzobit.Domain.Users;
using Fitzobit.Infrastructure;
using Fitzobit.Infrastructure.Persistence;
using Fitzobit.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.UserSecrets;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
// Development only: optional, git-ignored appsettings.Local.json in the project folder (e.g. the local database password)
// as an alternative to user secrets. Never published or copied into the Docker image.
// Order: after appsettings*.json and user secrets, before the (unprefixed) environment variables and the command line, so a
// real environment variable still wins. (The DOTNET_/ASPNETCORE_-prefixed sources come before appsettings.json; not those.)
if (builder.Environment.IsDevelopment())
{
    var localSettings = new JsonConfigurationSource
    {
        Path = "appsettings.Local.json", Optional = true, ReloadOnChange = true, FileProvider = builder.Environment.ContentRootFileProvider
    };
    var sources = builder.Configuration.Sources;
    var environment = sources.OfType<EnvironmentVariablesConfigurationSource>().LastOrDefault(s => string.IsNullOrEmpty(s.Prefix));
    sources.Insert(environment is null ? sources.Count : sources.IndexOf(environment), localSettings);
}

// A missing connection string stops the start with the environment and the user-secrets file this run looked for.
if (string.IsNullOrWhiteSpace(builder.Configuration["Database:ConnectionString"]))
{
    var secretsId = typeof(Program).Assembly.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;
    var secretsFile = secretsId is null ? "(no UserSecretsId)" : PathHelper.GetSecretsPathFromSecretsId(secretsId);
    throw new InvalidOperationException($"{DatabaseConfigurator.MissingConnectionStringMessage} (Environment: " +
        $"{builder.Environment.EnvironmentName}; user secrets file: {secretsFile} [{(File.Exists(secretsFile) ? "found" : "not found")}])");
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddHostedService<GeocodingWorker>();
builder.Services.AddHostedService<CustomerAutomationWorker>();

// ---------- Authentication: HttpOnly, SameSite=Strict cookie; sessions re-validated against the user's security stamp ----------
var requireHttpsCookies = builder.Configuration.GetValue("Auth:RequireHttpsCookies", !builder.Environment.IsDevelopment());
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "fitzobit.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = requireHttpsCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(builder.Configuration.GetValue("Auth:SessionHours", 10));
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var auth = ctx.HttpContext.RequestServices.GetRequiredService<AuthService>();
            var userId = ctx.Principal?.UserId();
            var user = userId is null ? null
                : await auth.ValidateSessionAsync(userId.Value, ctx.Principal!.FindFirst(FitzobitClaims.SecurityStamp)?.Value);
            if (user is null)
            {
                ctx.RejectPrincipal();
                await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(Policies.Operate, p => p.RequireRole(Roles.Admin, Roles.Manager));
    o.AddPolicy(Policies.Admin, p => p.RequireRole(Roles.Admin));
    o.AddPolicy(Policies.Money, p => p.RequireRole(Roles.Manager));
    // Everything requires a signed-in user unless explicitly marked [AllowAnonymous].
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// ---------- CSRF: double-submit token for every state-changing API call ----------
builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "fitzobit.af";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    // "Always" would make every request that reaches the app as plain HTTP fail (behind a TLS-terminating proxy, or
    // http://localhost). The token cookie is Secure whenever the request is HTTPS; the sign-in cookie stays strict.
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// Tunnels and reverse proxies on this machine (Tailscale Funnel, Cloudflare Tunnel, IIS) pass on the visitor's address and
// scheme. Only proxies on this machine are trusted: the default known proxies/networks are loopback.
// On a platform whose own proxy is the only way in (Render), set ForwardedHeaders:TrustAllProxies=true.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (builder.Configuration.GetValue("ForwardedHeaders:TrustAllProxies", false))
    {
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    }
});

// Cookie-protection keys live in the database, so sign-ins survive restarts and redeploys (hosts without a lasting disk).
builder.Services.AddDataProtection().SetApplicationName("Fitzobit").PersistKeysToDbContext<FitzobitDbContext>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.AddControllers(o => o.Filters.Add<ApiAntiforgeryFilter>())
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment()) app.UseHsts();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseDefaultFiles();
app.UseStaticFiles(StaticFileCaching.Options); // index.html never cached, so a normal reload shows a new deploy

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous().DisableRateLimiting();
app.MapFallback("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html", StaticFileCaching.Options).AllowAnonymous();

app.Run();

public partial class Program;
