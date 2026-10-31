using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using JuDesk.Api.Auth;
using JuDesk.Api.Data;
using JuDesk.Api.Endpoints;
using JuDesk.Api.Services;
using JuDesk.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<SeedOptions>(builder.Configuration.GetSection(SeedOptions.Section));
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ScheduleService>();
builder.Services.AddScoped<SetupService>();
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection(DemoOptions.Section));
builder.Services.AddSingleton<DemoState>();
builder.Services.AddScoped<DemoService>();
if (builder.Configuration.GetValue("Demo:Mode", false))
{
    builder.Services.AddHostedService<DemoResetService>();
}
builder.Services.AddJuDeskAuth();
builder.Services.AddProblemDetails();

// Behind the nginx of docker-compose (or any reverse proxy) the client address and scheme come
// from X-Forwarded-* headers. Only trust them when configured, because a spoofed header would
// otherwise defeat the per-address login limit. ForwardLimit is how many proxies sit in front:
// 1 for nginx alone, 2 when a tunnel or CDN in front of nginx adds the real client address.
var trustProxy = builder.Configuration.GetValue("Proxy:TrustForwardedHeaders", false);
var forwardLimit = builder.Configuration.GetValue("Proxy:ForwardLimit", 1);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = Math.Max(1, forwardLimit);
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Session cookies survive a container restart when the keys live on a volume (see docker-compose.yml).
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath)).SetApplicationName("JuDesk");
}
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Brute-force protection on login: a small fixed window per client address.
var loginAttemptsPerMinute = builder.Configuration.GetValue("Auth:LoginAttemptsPerMinute", 10);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = loginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "JuDesk API",
        Version = "v1",
        Description = "Shift scheduling for field technicians: skills, sites, shifts, rule-checked assignments and a week auto-scheduler.",
    });
});

var app = builder.Build();

if (trustProxy)
{
    app.UseForwardedHeaders();
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "JuDesk API v1"));

app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup("/api").RequireAuthorization();
api.MapSetup();
api.MapAuth();
api.MapCatalog();
api.MapShifts();
api.MapSchedule();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var seed = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SeedOptions>>().Value;
    var setup = scope.ServiceProvider.GetRequiredService<SetupService>();
    await db.Database.MigrateAsync();

    // `dotnet JuDesk.Api.dll --regen-setup-code` (scripts/regen-setup-code.sh) prints a fresh code and exits.
    if (args.Contains("--regen-setup-code"))
    {
        if (!await setup.NeedsSetupAsync())
        {
            Console.WriteLine("Setup is already complete; there is no code to regenerate.");
            return;
        }
        Console.WriteLine(SetupService.Banner(await setup.IssueCodeAsync()));
        return;
    }

    await DemoSeeder.SeedAsync(db, seed, app.Logger);

    if (await setup.IssueCodeIfNeededAsync() is { } code)
    {
        app.Logger.LogWarning("{Banner}", SetupService.Banner(code));
    }
}

app.Run();

/// <summary>Lets the integration tests reference the entry point with WebApplicationFactory.</summary>
public partial class Program;
