using NobodyFalls.Application.Abstractions;
using NobodyFalls.Application.Dev;
using NobodyFalls.Domain.Common;
using NobodyFalls.Infrastructure.Persistence;

namespace NobodyFalls.Api.Hosting;

internal static class StartupTasks
{
    /// <summary>
    /// Applies migrations when configured (development, docker compose). In production
    /// migrations are a deployment step, not something every instance races to do.
    /// In development, a first season opens today so the game is playable at once.
    /// </summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app, DevToolsOptions devTools)
    {
        if (!app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);

        if (devTools.Enabled)
        {
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            await scope.ServiceProvider.GetRequiredService<IDevStore>().EnsureSeasonAsync(GameDay.Of(clock.UtcNow), CancellationToken.None);
        }
    }
}
