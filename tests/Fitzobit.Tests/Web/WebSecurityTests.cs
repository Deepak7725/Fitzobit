using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Fitzobit.Tests.Web;

/// <summary>Authentication, role-based authorization, CSRF, lockout, headers and safe errors through the real HTTP pipeline.</summary>
public class WebSecurityTests
{
    private const string ManagerPassword = "Manager-Pass-77";

    private static async Task<ApiClient> CreateManagerAsync(WebTestHost host, ApiClient admin)
    {
        HttpAssert.Status(HttpStatusCode.OK, await admin.PostAsync("/api/users",
            new { userName = "meena", displayName = "Meena Manager", role = "Manager", password = ManagerPassword }));
        return await host.SignInAsync("meena", ManagerPassword);
    }

    [Fact]
    public async Task Anonymous_users_only_reach_the_auth_state()
    {
        using var host = new WebTestHost();
        var client = host.NewClient();

        var state = await client.RefreshAsync();
        Assert.True(state.GetProperty("setupRequired").GetBoolean());
        Assert.True(state.GetProperty("setupAllowedHere").GetBoolean());
        Assert.False(state.GetProperty("authenticated").GetBoolean());

        foreach (var url in new[] { "/api/customers", "/api/dashboard", "/api/dictionaries", "/api/delivery-groups", "/api/settings", "/api/users", "/api/system" })
            HttpAssert.Status(HttpStatusCode.Unauthorized, await client.GetAsync(url));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await client.PostAsync("/api/customers/export", new { scope = "All" }));
    }

    [Fact]
    public async Task First_run_setup_works_once_and_signs_in_the_administrator()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();

        var state = await admin.RefreshAsync();
        Assert.False(state.GetProperty("setupRequired").GetBoolean());
        Assert.True(state.GetProperty("authenticated").GetBoolean());
        Assert.Equal(("owner", "Admin"), (state.GetProperty("user").GetProperty("userName").GetString(), state.GetProperty("user").GetProperty("role").GetString()));

        foreach (var url in new[] { "/api/users", "/api/audit", "/api/audit/actions", "/api/system", "/api/dictionaries/MealType", "/api/customers", "/api/settings" })
            HttpAssert.Status(HttpStatusCode.OK, await admin.GetAsync(url));

        // The first admin is attributed to themselves (not "anonymous") and counts as signed in.
        var audit = await (await admin.GetAsync("/api/audit")).Content.ReadFromJsonAsync<JsonElement>();
        var created = audit.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("action").GetString() == "UserCreated");
        Assert.Equal("owner", created.GetProperty("performedBy").GetString());
        var users = await (await admin.GetAsync("/api/users")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, users[0].GetProperty("lastLoginAt").ValueKind);

        var stranger = host.NewClient();
        await stranger.RefreshAsync();
        var again =await stranger.PostAsync("/api/auth/setup", new { userName = "intruder", displayName = "Intruder", password = "Intruder-Pass-1" });
        HttpAssert.Status(HttpStatusCode.Conflict, again);
        Assert.Equal("Setup has already been completed.", await ApiClient.DetailAsync(again));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await stranger.GetAsync("/api/customers"));
    }

    [Fact]
    public async Task Setup_from_a_remote_address_is_refused_unless_allowed()
    {
        using var host = new WebTestHost(allowRemoteSetup: false);
        var client = host.NewClient();

        var state = await client.RefreshAsync();
        Assert.True(state.GetProperty("setupRequired").GetBoolean());
        Assert.False(state.GetProperty("setupAllowedHere").GetBoolean());
        HttpAssert.Status(HttpStatusCode.Forbidden,
            await client.PostAsync("/api/auth/setup", new { userName = "owner", displayName = "Owner", password = ApiClient.AdminPassword }));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("X-Forwarded-For", "203.0.113.7", false)]
    [InlineData("Tailscale-Funnel-Request", "?1", false)]
    [InlineData("CF-Connecting-IP", "203.0.113.7", false)]
    public async Task Setup_is_local_only_and_never_through_a_tunnel(string? header, string? value, bool allowed)
    {
        using var host = new WebTestHost(allowRemoteSetup: false, loopbackClient: true);
        var client = host.NewClient();
        if (header is not null) client.Http.DefaultRequestHeaders.Add(header, value);

        var state = await client.RefreshAsync();
        Assert.Equal(allowed, state.GetProperty("setupAllowedHere").GetBoolean());
        var setup = await client.PostAsync("/api/auth/setup", new { userName = "owner", displayName = "Owner", password = ApiClient.AdminPassword });
        HttpAssert.Status(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, setup);
    }

    [Fact]
    public async Task Remote_setup_with_a_setup_code_needs_the_exact_code()
    {
        using var host = new WebTestHost(allowRemoteSetup: false, setupCode: "Xk7-secret-setup-code");
        var client = host.NewClient();

        var state = await client.RefreshAsync();
        Assert.True(state.GetProperty("setupAllowedHere").GetBoolean());
        Assert.True(state.GetProperty("setupCodeRequired").GetBoolean());

        HttpAssert.Status(HttpStatusCode.Forbidden, await client.PostAsync("/api/auth/setup",
            new { userName = "owner", displayName = "Owner", password = ApiClient.AdminPassword }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await client.PostAsync("/api/auth/setup",
            new { userName = "owner", displayName = "Owner", password = ApiClient.AdminPassword, setupCode = "wrong" }));
        HttpAssert.Status(HttpStatusCode.OK, await client.PostAsync("/api/auth/setup",
            new { userName = "owner", displayName = "Owner", password = ApiClient.AdminPassword, setupCode = " Xk7-secret-setup-code " }));

        var after = await client.RefreshAsync();
        Assert.False(after.GetProperty("setupRequired").GetBoolean());
        Assert.False(after.GetProperty("setupCodeRequired").GetBoolean());
    }

    [Fact]
    public async Task Health_endpoint_is_public_and_needs_no_session()
    {
        using var host = new WebTestHost();
        var response = await host.NewClient().GetAsync("/api/health");
        HttpAssert.Status(HttpStatusCode.OK, response);
        Assert.Contains("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Plain_http_behind_a_tls_proxy_still_works()
    {
        using var host = new WebTestHost();
        var client = new ApiClient(host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") }));

        var state = await client.RefreshAsync();
        Assert.True(state.GetProperty("setupRequired").GetBoolean());
    }

    [Fact]
    public async Task Only_manager_accounts_reach_the_money_page()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();
        var manager = await CreateManagerAsync(host, admin);

        foreach (var url in new[] { "/api/money/settings", "/api/money/prices", "/api/money/earnings" })
        {
            HttpAssert.Status(HttpStatusCode.OK, await manager.GetAsync(url));
            HttpAssert.Status(HttpStatusCode.Forbidden, await admin.GetAsync(url));
        }
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync("/api/money/prices", new { prices = Array.Empty<object>() }));
        HttpAssert.Status(HttpStatusCode.OK, await manager.PutAsync("/api/money/prices", new { prices = Array.Empty<object>() }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PostAsync("/api/money/prices/fill-missing", new { }));
        HttpAssert.Status(HttpStatusCode.OK, await manager.PostAsync("/api/money/prices/fill-missing", new { }));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await host.NewClient().GetAsync("/api/money/settings"));
    }

    [Fact]
    public async Task Managers_operate_and_change_settings_while_admins_only_view_them()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();
        var manager = await CreateManagerAsync(host, admin);

        foreach (var url in new[] { "/api/customers", "/api/customers/filter-options", "/api/dashboard", "/api/dictionaries", "/api/delivery-groups", "/api/settings" })
            HttpAssert.Status(HttpStatusCode.OK, await manager.GetAsync(url));
        HttpAssert.Status(HttpStatusCode.OK, await manager.PostAsync("/api/delivery-groups", new { count = 2 }));

        var export = await manager.PostAsync("/api/customers/export", new { scope = "Filtered", filter = new { pincode = "641002" } });
        HttpAssert.Status(HttpStatusCode.OK, export);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.Content.Headers.ContentType?.MediaType);
        Assert.Equal("0", export.Headers.GetValues("X-Export-Rows").Single());
        var pdf = await manager.PostAsync("/api/customers/export",
            new { scope = "Filtered", format = "Pdf", filter = new { pincode = "641002" }, columns = new[] { "customerName", "phone" } });
        HttpAssert.Status(HttpStatusCode.OK, pdf);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));
        HttpAssert.Status(HttpStatusCode.Conflict, await manager.PostAsync("/api/customers/export", new { columns = new[] { "secret" } }));

        // both roles see users, audit log, system info, drop-down values and the days paused for everyone
        foreach (var url in new[] { "/api/users", "/api/audit", "/api/system", "/api/dictionaries/MealType", "/api/festive-pauses" })
        {
            HttpAssert.Status(HttpStatusCode.OK, await manager.GetAsync(url));
            HttpAssert.Status(HttpStatusCode.OK, await admin.GetAsync(url));
        }

        // only Managers change them (owner, 2026-11-12)
        var bowl = await manager.PostAsync("/api/dictionaries/MealType", new { displayName = "Protein Punch Meal Box" });
        HttpAssert.Status(HttpStatusCode.OK, bowl);
        var bowlId = (await bowl.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        HttpAssert.Status(HttpStatusCode.OK, await manager.PutAsync($"/api/dictionaries/entries/{bowlId}", new { displayName = "Protein Punch Box", isActive = true }));
        HttpAssert.Status(HttpStatusCode.OK, await manager.PostAsync("/api/users",
            new { userName = "siva", displayName = "Siva Admin", role = "Admin", password = "Thanjavur-Box-26" }));

        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PostAsync("/api/users",
            new { userName = "sneaky", displayName = "Sneaky", role = "Manager", password = "Sneaky-Pass-99" }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PostAsync("/api/dictionaries/MealType", new { displayName = "Sneaky Bowl" }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync($"/api/dictionaries/entries/{bowlId}", new { displayName = "Renamed", isActive = false }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync("/api/dictionaries/MealType/order", new { ids = new[] { bowlId } }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.Http.DeleteAsync($"/api/dictionaries/entries/{bowlId}"));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync("/api/settings/delivery-area", new { city = "Thanjavur" }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync("/api/settings/renewal", new { daysBefore = 3, message = "Hi {name}" }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PostAsync("/api/festive-pauses/preview", new { dates = new[] { "2030-01-07" } }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PostAsync("/api/festive-pauses", new { dates = new[] { "2030-01-07" } }));
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.Http.DeleteAsync("/api/festive-pauses/1"));
        HttpAssert.Status(HttpStatusCode.OK, await manager.PutAsync("/api/settings/renewal", new { daysBefore = 4, message = "Hi {name}, renew?" }));

        var me = await manager.RefreshAsync();
        Assert.Equal("Manager", me.GetProperty("user").GetProperty("role").GetString());

        var audit = await (await admin.GetAsync("/api/audit?action=UserSignedIn")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("performedBy").GetString() == "meena");
        Assert.DoesNotContain(audit.GetProperty("items").EnumerateArray(), a => a.GetProperty("performedBy").GetString() == "anonymous");
    }

    [Fact]
    public async Task State_changing_requests_need_the_csrf_header()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();

        var token = admin.Http.DefaultRequestHeaders.GetValues("X-XSRF-TOKEN").Single();
        admin.Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        HttpAssert.Status(HttpStatusCode.BadRequest, await admin.PostAsync("/api/delivery-groups", new { count = 1, notes = "No Token Group" }));

        admin.Http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "forged-token");
        HttpAssert.Status(HttpStatusCode.BadRequest, await admin.PostAsync("/api/delivery-groups", new { count = 1, notes = "Forged Token Group" }));

        admin.Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        admin.Http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        HttpAssert.Status(HttpStatusCode.OK, await admin.PostAsync("/api/delivery-groups", new { count = 1, notes = "With Token Group" }));

        var groups = await (await admin.GetAsync("/api/delivery-groups")).Content.ReadAsStringAsync();
        Assert.Contains("With Token Group", groups);
        Assert.DoesNotContain("No Token Group", groups);
    }

    [Fact]
    public async Task Failed_sign_ins_are_generic_and_lock_the_account_after_five_attempts()
    {
        using var host = new WebTestHost();
        await host.SetupAdminAsync();
        var client = host.NewClient();
        await client.RefreshAsync();

        var unknown = await client.PostAsync("/api/auth/login", new { userName = "nobody", password = "whatever-123" });
        HttpAssert.Status(HttpStatusCode.Unauthorized, unknown);
        Assert.Equal("Invalid user name or password.", await ApiClient.DetailAsync(unknown));

        for (var i = 0; i < 5; i++)
        {
            var wrong = await client.PostAsync("/api/auth/login", new { userName = "owner", password = $"Wrong-Pass-{i}x" });
            HttpAssert.Status(HttpStatusCode.Unauthorized, wrong);
            Assert.Equal("Invalid user name or password.", await ApiClient.DetailAsync(wrong));
        }

        var locked = await client.PostAsync("/api/auth/login", new { userName = "owner", password = ApiClient.AdminPassword });
        HttpAssert.Status(HttpStatusCode.Unauthorized, locked);
        Assert.StartsWith("Too many failed attempts", await ApiClient.DetailAsync(locked));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await client.GetAsync("/api/customers"));
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions_but_keeps_this_one()
    {
        using var host = new WebTestHost();
        var first = await host.SetupAdminAsync();
        var second = await host.SignInAsync("owner", ApiClient.AdminPassword);
        HttpAssert.Status(HttpStatusCode.OK, await second.GetAsync("/api/users"));

        HttpAssert.Status(HttpStatusCode.Conflict, await first.PostAsync("/api/auth/change-password",
            new { currentPassword = "not-the-password-1", newPassword = "Changed-Pass-2026" }));
        HttpAssert.Status(HttpStatusCode.NoContent, await first.PostAsync("/api/auth/change-password",
            new { currentPassword = ApiClient.AdminPassword, newPassword = "Changed-Pass-2026" }));

        HttpAssert.Status(HttpStatusCode.OK, await first.GetAsync("/api/users"));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await second.GetAsync("/api/users"));

        await first.RefreshAsync();
        HttpAssert.Status(HttpStatusCode.NoContent, await first.PostAsync("/api/auth/logout", new { }));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await first.GetAsync("/api/users"));
    }

    [Fact]
    public async Task Deactivating_a_user_ends_their_session()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();
        var manager = await CreateManagerAsync(host, admin);
        var users = await (await admin.GetAsync("/api/users")).Content.ReadFromJsonAsync<JsonElement>();
        var managerId = users.EnumerateArray().Single(u => u.GetProperty("userName").GetString() == "meena").GetProperty("id").GetInt32();
        var ownerId = users.EnumerateArray().Single(u => u.GetProperty("userName").GetString() == "owner").GetProperty("id").GetInt32();

        // only a Manager changes users: the Admin cannot deactivate anyone
        HttpAssert.Status(HttpStatusCode.Forbidden, await admin.PutAsync($"/api/users/{managerId}", new { displayName = "Meena Manager", role = "Manager", isActive = false }));
        HttpAssert.Status(HttpStatusCode.NoContent, await manager.PutAsync($"/api/users/{ownerId}", new { displayName = "Owner", role = "Admin", isActive = false }));
        HttpAssert.Status(HttpStatusCode.Unauthorized, await admin.GetAsync("/api/customers"));

        var self = await manager.PutAsync($"/api/users/{managerId}", new { displayName = "Meena Manager", role = "Admin", isActive = true });
        HttpAssert.Status(HttpStatusCode.Conflict, self);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_hide_internal_errors()
    {
        using var host = new WebTestHost();
        var client = host.NewClient();

        var state = await client.GetAsync("/api/auth/state");
        Assert.Equal("nosniff", state.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", state.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", state.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", state.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(state.Headers.CacheControl?.NoStore);
        Assert.False(state.Headers.Contains("Server"));
        var cookies = state.Headers.GetValues("Set-Cookie").ToList();
        Assert.Contains(cookies, c => c.StartsWith("fitzobit.af=") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase) && c.Contains("secure"));

        var error = await client.GetAsync("/api/test-only/throw");
        HttpAssert.Status(HttpStatusCode.InternalServerError, error);
        var body = await error.Content.ReadAsStringAsync();
        Assert.Contains("reference", body);
        Assert.DoesNotContain("Secret internal detail", body);
        Assert.DoesNotContain("secret-password", body);
        Assert.DoesNotContain("   at ", body);

        HttpAssert.Status(HttpStatusCode.NotFound, await client.GetAsync("/api/does-not-exist"));
    }

    [Fact]
    public async Task Settings_expose_no_secrets_and_report_whatsapp_not_configured()
    {
        using var host = new WebTestHost();
        var admin = await host.SetupAdminAsync();

        var body = await (await admin.GetAsync("/api/settings")).Content.ReadAsStringAsync();
        var settings = JsonDocument.Parse(body).RootElement;
        Assert.Equal("WhatsApp integration not configured", settings.GetProperty("whatsApp").GetProperty("message").GetString());
        Assert.False(settings.GetProperty("whatsApp").GetProperty("configured").GetBoolean());
        Assert.Contains("Delivery Time", settings.GetProperty("export").GetProperty("columns").EnumerateArray().Select(c => c.GetString()));
        Assert.DoesNotContain("Password=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("accessToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fitzobit-web-test", body);
    }
}
