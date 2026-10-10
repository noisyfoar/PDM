using Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.PostgreSql;

public sealed class PostgresDatabase : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly DbContextOptions<PdmDbContext> _options;

    public PostgresDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _options = new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql(_dataSource).Options;
    }

    public IUnitOfWork CreateUnitOfWork() => new UnitOfWork(new PdmDbContext(_options));

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var context = new PdmDbContext(_options);
        await context.Database.MigrateAsync(ct);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
