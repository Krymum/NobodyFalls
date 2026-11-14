using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace NobodyFalls.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations (start-up in development, tests, deployment step).</summary>
public sealed class DatabaseMigrator
{
    private readonly NobodyFallsDbContext _db;

    internal DatabaseMigrator(NobodyFallsDbContext db) => _db = db;

    public Task MigrateAsync(CancellationToken cancellationToken) => _db.Database.MigrateAsync(cancellationToken);
}

/// <summary>Lets <c>dotnet ef</c> build the context without starting the API.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NobodyFallsDbContext>
{
    public NobodyFallsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NOBODYFALLS_DB")
            ?? "Host=localhost;Port=5432;Database=nobodyfalls;Username=nobodyfalls;Password=nobodyfalls";
        var builder = new DbContextOptionsBuilder<NobodyFallsDbContext>();
        builder.UseNobodyFalls(new NpgsqlDataSourceBuilder(connectionString).Build());
        return new NobodyFallsDbContext(builder.Options);
    }
}
