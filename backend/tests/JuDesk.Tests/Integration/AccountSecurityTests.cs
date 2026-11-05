using System.Net;
using System.Net.Http.Json;
using System.Text;
using JuDesk.Api.Contracts;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;

namespace JuDesk.Tests.Integration;

/// <summary>Sessions that really end, password changes and resets, the password policy and the login lockout.</summary>
[Collection(ApiCollection.Name)]
public class AccountSecurityTests(ApiFixture fixture)
{
    private HttpClient CookielessClient() => fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private async Task<HttpResponseMessage> WithCookieAsync(string cookie, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return await CookielessClient().SendAsync(request);
    }

    [Fact]
    public async Task A_copied_cookie_stops_working_after_logout()
    {
        var (user, password) = await fixture.CreatePlannerAsync();
        var client = fixture.AnonymousClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        var stolen = ApiFixture.AuthCookie(login);
        Assert.Equal(HttpStatusCode.OK, (await WithCookieAsync(stolen, "/api/technicians")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await WithCookieAsync(stolen, "/api/technicians")).StatusCode);
    }

    [Fact]
    public async Task Logging_out_one_device_keeps_the_others()
    {
        var (user, password) = await fixture.CreatePlannerAsync();
        var phone = await fixture.LoginAsync(user.Email, password);
        var laptop = await fixture.LoginAsync(user.Email, password);

        await phone.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_the_other_devices()
    {
        var (user, password) = await fixture.CreatePlannerAsync();
        var mine = await fixture.LoginAsync(user.Email, password);
        var other = await fixture.LoginAsync(user.Email, password);
        const string next = "Velvet-Compass-Ninety-1";

        var wrong = await mine.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest("not-my-password", next));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("currentPassword", await wrong.Content.ReadAsStringAsync());

        var weak = await mine.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(password, "123456789012"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Contains("newPassword", await weak.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NoContent, (await mine.PostAsJsonAsync("/api/auth/password", new ChangePasswordRequest(password, next))).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await mine.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/auth/me")).StatusCode);
        var anonymous = fixture.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, next))).StatusCode);
    }

    [Fact]
    public async Task An_admin_reset_ends_every_session_of_the_user()
    {
        var (user, password) = await fixture.CreatePlannerAsync();
        var planner = await fixture.LoginAsync(user.Email, password);
        var admin = await fixture.AdminClientAsync();
        const string next = "Amber-Lighthouse-Sixty-2";

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/users/{user.Id}/password", new ResetPasswordRequest("short"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/users/{Guid.NewGuid()}/password", new ResetPasswordRequest(next))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await planner.PostAsJsonAsync($"/api/users/{user.Id}/password", new ResetPasswordRequest(next))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/users/{user.Id}/password", new ResetPasswordRequest(next))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await planner.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
        await fixture.LoginAsync(user.Email, next);
    }

    [Theory]
    [InlineData("12345678")]            // the old minimum
    [InlineData("123456789012")]        // a digit run
    [InlineData("password1234")]        // on the common list
    [InlineData("aaaaaaaaaaaa")]        // one character
    [InlineData("Pat-Planner-Rocks")]   // contains the user's name
    public async Task Weak_passwords_are_refused(string password)
    {
        var admin = await fixture.AdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest($"weak-{Guid.NewGuid():N}@test.local", "Pat Planner", password, UserRole.Planner));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var (user, password) = await fixture.CreatePlannerAsync();
        var client = fixture.AnonymousClient();

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, $"wrong-guess-{i}"));
        }
        var locked = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Contains("Too many failed attempts", await locked.Content.ReadAsStringAsync());

        // An admin reset lifts the lockout.
        var admin = await fixture.AdminClientAsync();
        const string next = "Cobalt-Saddle-Forty-4";
        (await admin.PostAsJsonAsync($"/api/users/{user.Id}/password", new ResetPasswordRequest(next))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, next))).StatusCode);
    }

    [Fact]
    public async Task The_lockout_does_not_reveal_which_emails_exist()
    {
        var client = fixture.AnonymousClient();
        var nobody = $"nobody-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(nobody, $"wrong-guess-{i}"));
        }
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(nobody, "anything-at-all"));

        Assert.Contains("Too many failed attempts", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("7")]
    [InlineData("\"7\"")]
    [InlineData("\"Superuser\"")]
    public async Task Roles_must_be_named(string role)
    {
        var admin = await fixture.AdminClientAsync();
        var json = $$"""{"email":"enum-{{Guid.NewGuid():N}}@test.local","displayName":"Enum","password":"Willow-Ferry-Ninety-3","role":{{role}}}""";

        var response = await admin.PostAsync("/api/users", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Config_is_public_and_swagger_is_off_by_default()
    {
        var client = fixture.AnonymousClient();

        var config = await client.GetFromJsonAsync<ConfigDto>("/api/config");
        Assert.Equal("UTC", config!.TimeZone);
        Assert.Equal(PasswordPolicy.MinLength, config.PasswordMinLength);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}
