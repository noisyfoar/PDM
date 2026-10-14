using Infrastructure.Persistence;
using Infrastructure.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Tests;

// Каждый тест получает собственную схему, которую удаляет после завершения.
internal sealed class PostgresTestDatabase : IAsyncDisposable
{
    private readonly string _baseConnectionString;
    private readonly string _schema = $"pdm_test_{Guid.NewGuid():N}";
    private bool _schemaCreated;
    public string ConnectionString { get; }
    public PostgresDatabase Database { get; }
    public DbContextOptions<PdmDbContext> Options => new DbContextOptionsBuilder<PdmDbContext>()
        .UseNpgsql(ConnectionString).Options;

    private PostgresTestDatabase(string connectionString)
    {
        _baseConnectionString = connectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = _schema }.ConnectionString;
        Database = new PostgresDatabase(ConnectionString);
    }

    public static async Task<PostgresTestDatabase> CreateAsync(bool migrate = true)
    {
        var connectionString = Environment.GetEnvironmentVariable("PDM_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Укажите PDM_TEST_CONNECTION_STRING для отдельной тестовой базы PostgreSQL.");
        var test = new PostgresTestDatabase(connectionString);
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{test._schema}\"", connection);
            await command.ExecuteNonQueryAsync();
            test._schemaCreated = true;
            if (migrate) await test.Database.InitializeAsync();
            return test;
        }
        catch
        {
            await test.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        if (!_schemaCreated) return;
        await using var connection = new NpgsqlConnection(_baseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
