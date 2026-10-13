using Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.PostgreSql;

public sealed class PostgresDatabase : IUnitOfWorkFactory, IAsyncDisposable
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

    public ICompositionReader CreateCompositionReader() => new PostgresCompositionReader(_options);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var context = new PdmDbContext(_options);
        await context.Database.MigrateAsync(ct);
        // Заполняем новый уникальный ключ для стандартных изделий из предыдущей схемы.
        await context.Objects.Where(obj => obj.ObjectType == Domain.Objects.ObjectType.StandardPart
                && EF.Property<string?>(obj, "StandardName") == null)
            .Include(obj => obj.Versions).LoadAsync(ct);
        if (context.ChangeTracker.Entries<Domain.Objects.PDMObject>().Any())
            await context.SaveChangesAsync(ct);
    }

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
