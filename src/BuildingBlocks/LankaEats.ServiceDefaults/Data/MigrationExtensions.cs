using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LankaEats.ServiceDefaults.Data;

public static class MigrationExtensions
{
    /// <summary>
    /// Applies pending EF Core migrations on startup, retrying while the database container starts.
    /// This is fine for a demo or a single replica. With several replicas, run migrations as a
    /// separate job (e.g. <c>dotnet ef migrations bundle</c>) so they don't race each other.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TDbContext>(this WebApplication app, int maxAttempts = 10)
        where TDbContext : DbContext
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
                await db.Database.MigrateAsync();
                logger.LogInformation("Database for {DbContext} is up to date", typeof(TDbContext).Name);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Migration attempt {Attempt}/{Max} failed; retrying", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }
}
