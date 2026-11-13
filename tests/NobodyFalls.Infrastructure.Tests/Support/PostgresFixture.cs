using Microsoft.Extensions.DependencyInjection;
using NobodyFalls.Infrastructure.Persistence;
using NobodyFalls.Infrastructure.Tests.Support;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresFixture))]

namespace NobodyFalls.Infrastructure.Tests.Support;

/// <summary>
/// One real PostgreSQL for the whole test run. Migrations are applied once to a
/// template database; every test then gets its own copy, which is fast and lets
/// tests run in parallel without sharing state.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private const string Template = "nobodyfalls_template";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private int _databases;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await ExecuteOnServerAsync($"create database {Template}");

        var services = new ServiceCollection()
            .AddInfrastructure(new InfrastructureOptions { ConnectionString = ConnectionString(Template), RunNightWorker = false })
            .BuildServiceProvider();
        await using (services)
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().MigrateAsync(CancellationToken.None);
        }

        // A template cannot be copied while someone is connected to it.
        NpgsqlConnection.ClearAllPools();
    }

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>A fresh, migrated database for one test.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Interlocked.Increment(ref _databases)}";
        await ExecuteOnServerAsync($"create database {name} template {Template}");
        return ConnectionString(name);
    }

    private string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString("postgres"));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
