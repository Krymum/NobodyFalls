using Microsoft.AspNetCore.HttpOverrides;
using NobodyFalls.Api.Admin;
using NobodyFalls.Api.Auth;
using NobodyFalls.Api.Endpoints;
using NobodyFalls.Api.Hosting;
using NobodyFalls.Api.Http;
using NobodyFalls.Api.Realtime;
using NobodyFalls.Application;
using NobodyFalls.Infrastructure;
using NobodyFalls.Infrastructure.Accounts;
using NobodyFalls.Infrastructure.Push;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var devTools = DevToolsOptions.From(configuration, builder.Environment);
var runWorkers = configuration.GetValue("Night:RunWorker", true);

var connectionString = configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
}

builder.Services
    .AddSingleton(devTools)
    .AddApplication()
    .AddInfrastructure(new InfrastructureOptions
    {
        ConnectionString = connectionString,
        SimulatedClock = devTools.Enabled,
        RunNightWorker = runWorkers,
    })
    .Configure<TurnstileOptions>(configuration.GetSection(TurnstileOptions.Section))
    .Configure<PushOptions>(configuration.GetSection(PushOptions.Section))
    .AddNobodyFallsRealtime(configuration, runWorkers)
    .AddNobodyFallsAuth(configuration, devTools.Enabled)
    .AddNobodyFallsRateLimits(configuration.GetSection("RateLimits").Get<RateLimitOptions>() ?? new())
    .AddProblemDetails()
    .AddOpenApi()
    .ConfigureHttpJsonOptions(options => Json.Configure(options.SerializerOptions))

    // A malformed body is the client's mistake: answer 400, also in Development.
    .Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);

if (configuration.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    // Behind a reverse proxy (Cloudflare, Caddy…): use the client's real IP for rate limits.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (configuration.GetValue<bool>("Proxy:TrustForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSpaStaticFiles();

// Explicitly after static files: built files are served before any route is matched,
// so that /admin/assets/… never falls into the admin's catch-all page route.
app.UseRouting();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", async (NpgsqlDataSource db, CancellationToken ct) =>
{
    await using var command = db.CreateCommand("select 1");
    await command.ExecuteScalarAsync(ct);
    return Results.Ok(new { status = "ok" });
}).ExcludeFromDescription();

app.MapAuthEndpoints(devTools);

var api = app.MapGroup("/api/v1").AddEndpointFilter<RequireApiHeader>().WithTags("Game");
api.MapSessionEndpoints();
api.MapGameEndpoints();
api.MapMeEndpoints();
api.MapFriendEndpoints();
api.MapPushEndpoints();

app.MapAdminEndpoints();

app.MapHub<GameHub>(GameHub.Path);

if (devTools.Enabled)
{
    app.MapGroup("/dev").AddEndpointFilter<RequireApiHeader>().MapDevEndpoints();
}

app.MapSpa();

await app.PrepareDatabaseAsync(devTools);

// Operator commands (e.g. `staff grant <pseudo>`) run once and exit instead of serving.
if (await StaffCommand.TryRunAsync(app, args))
{
    return;
}

await app.RunAsync();

/// <summary>Entry point, public for integration tests (WebApplicationFactory).</summary>
public partial class Program;
