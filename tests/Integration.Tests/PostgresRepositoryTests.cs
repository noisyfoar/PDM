using Domain.Objects;
using Infrastructure.Persistence.PostgreSql;
using Xunit;

namespace Infrastructure.Tests;

[Collection("postgres")]
[Trait("Category", "Postgres")]
public sealed class PostgresRepositoryTests : IAsyncLifetime
{
    private PostgresTestDatabase _test = null!;
    private PostgresDatabase _database => _test.Database;

    public async Task InitializeAsync() => _test = await PostgresTestDatabase.CreateAsync();
    public async Task DisposeAsync() { if (_test is not null) await _test.DisposeAsync(); }

    [Fact]
    public async Task SaveAndLoad_PreservesObjectAndVersion()
    {
        var designation = Designation.Create($"АБВГ.{Random.Shared.Next(100000, 999999)}.{Random.Shared.Next(100, 999)}");
        var source = new PDMObject(ObjectType.Part, designation, "Тестовая деталь", "Сталь", 1.25m);

        await using (var work = _database.CreateUnitOfWork())
        {
            await work.BeginAsync();
            work.Objects.Add(source);
            await work.CommitAsync();
        }

        await using var read = _database.CreateUnitOfWork();
        await read.BeginAsync();
        var loaded = await read.Objects.GetByIdAsync(source.Id);

        Assert.NotNull(loaded);
        Assert.Equal(source.Id, loaded!.Id);
        Assert.Equal("Тестовая деталь", loaded.Name);
        Assert.Equal(1.25m, loaded.CurrentVersion!.MassKg);
        await read.RollbackAsync();
    }

    [Fact]
    public async Task Rollback_DoesNotPersistObject()
    {
        var source = new PDMObject(ObjectType.StandardPart, null, $"Тестовый стандарт {Guid.NewGuid():N}", massKg: 0.1m);

        await using (var work = _database.CreateUnitOfWork())
        {
            await work.BeginAsync();
            work.Objects.Add(source);
            await work.RollbackAsync();
        }

        await using var read = _database.CreateUnitOfWork();
        await read.BeginAsync();
        Assert.Null(await read.Objects.GetByIdAsync(source.Id));
        await read.RollbackAsync();
    }

    [Fact]
    public async Task Search_FindsByDesignationAndName()
    {
        var marker = Guid.NewGuid().ToString("N");
        var part = new PDMObject(ObjectType.Part,
            Designation.Create($"АБВГ.{Random.Shared.Next(100000, 999999)}.{Random.Shared.Next(100, 999)}"),
            $"Деталь {marker}", "Сталь", 2m);

        await using (var work = _database.CreateUnitOfWork())
        {
            await work.BeginAsync();
            work.Objects.Add(part);
            await work.CommitAsync();
        }

        await using var read = _database.CreateUnitOfWork();
        await read.BeginAsync();
        var found = await read.Objects.SearchAsync(marker);
        Assert.Contains(found, x => x.Id == part.Id);
        await read.RollbackAsync();
    }
}

[CollectionDefinition("postgres", DisableParallelization = true)]
public sealed class PostgresCollection;
