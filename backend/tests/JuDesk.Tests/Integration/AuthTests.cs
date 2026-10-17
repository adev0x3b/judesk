using System.Net;
using System.Net.Http.Json;
using JuDesk.Api.Contracts;
using JuDesk.Domain.Entities;

namespace JuDesk.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFixture fixture)
{
    [Fact]
    public async Task Api_requires_a_session()
    {
        var client = fixture.AnonymousClient();

        var response = await client.GetAsync("/api/schedule/week");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location); // an API, not a login-page redirect
    }

    [Fact]
    public async Task Health_is_public()
    {
        var response = await fixture.AnonymousClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_sets_a_hardened_cookie_and_me_returns_the_user()
    {
        var client = fixture.AnonymousClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFixture.AdminEmail.ToUpperInvariant(), ApiFixture.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("judesk.auth=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");
        Assert.Equal(ApiFixture.AdminEmail, me!.Email);
        Assert.Equal(UserRole.Admin, me.Role);

        await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_a_cookie()
    {
        var client = fixture.AnonymousClient();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(ApiFixture.AdminEmail, "nope"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Admin_creates_a_planner_who_cannot_manage_users()
    {
        var admin = await fixture.AdminClientAsync();
        var email = $"planner-{Guid.NewGuid():N}@test.local";

        var created = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(email, "Planner", "Planner-Pass-1", UserRole.Planner));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var planner = fixture.AnonymousClient();
        var login = await planner.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Planner-Pass-1"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await planner.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync("/api/technicians")).StatusCode);
    }

    [Fact]
    public async Task Weak_passwords_and_duplicate_emails_are_rejected()
    {
        var admin = await fixture.AdminClientAsync();

        var weak = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("x@test.local", "X", "short", UserRole.Planner));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var duplicate = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(ApiFixture.AdminEmail, "Dup", "Long-Enough-1", UserRole.Planner));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Session_of_a_deleted_user_is_rejected_and_the_cookie_cleared()
    {
        var admin = await fixture.AdminClientAsync();
        var email = $"gone-{Guid.NewGuid():N}@test.local";
        var created = await (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(email, "Gone", "Gone-Pass-1", UserRole.Planner)))
            .Content.ReadFromJsonAsync<UserDto>();

        var planner = fixture.AnonymousClient();
        Assert.Equal(HttpStatusCode.OK, (await planner.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Gone-Pass-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await planner.GetAsync("/api/technicians")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{created!.Id}")).StatusCode);

        // The cookie still verifies cryptographically, but its user is gone: 401 and the cookie is deleted.
        var response = await planner.GetAsync("/api/technicians");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith("judesk.auth=;", cookie);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await planner.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers()
    {
        var response = await fixture.AnonymousClient().GetAsync("/health");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
    }
}
