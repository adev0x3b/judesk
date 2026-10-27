using JuDesk.Api.Auth;
using JuDesk.Api.Contracts;
using JuDesk.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JuDesk.Api.Endpoints;

public static class SetupEndpoints
{
    public static RouteGroupBuilder MapSetup(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/setup").WithTags("Setup").AllowAnonymous();

        group.MapGet("/status", async (SetupService setup, CancellationToken ct) =>
            new SetupStatusDto(await setup.NeedsSetupAsync(ct)))
            .Produces<SetupStatusDto>();

        group.MapPost("/", async (SetupRequest request, SetupService setup, HttpContext http, CancellationToken ct) =>
        {
            var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            var v = new Validation()
                .Require("code", request.Code, 40)
                .Require("email", email)
                .Must("email", email.Contains('@'), "Must be an email address.")
                .Require("displayName", request.DisplayName, 120)
                .Must("password", request.Password?.Length >= 8, "Must be at least 8 characters.");
            if (v.Problem() is { } problem)
            {
                return problem;
            }

            var (result, admin) = await setup.CompleteAsync(request.Code!, email, request.DisplayName!, request.Password!, ct);
            if (result == SetupResult.AlreadyDone)
            {
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Setup is already complete.");
            }
            if (result == SetupResult.WrongCode || admin is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Wrong setup code. Check the API log or run scripts/regen-setup-code.sh.");
            }

            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, admin.ToPrincipal());
            return Results.Created("/api/auth/me", admin.ToDto());
        })
        .RequireRateLimiting(AuthEndpoints.LoginRateLimitPolicy)
        .Produces<UserDto>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status409Conflict);

        return api;
    }
}
