using System.Data.Common;
using Application.Services;
using Application.Records.Calculations;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Domain.Components;
using Domain.Objects;
using Infrastructure.Persistence;
using Infrastructure.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Infrastructure.Tests;

[Collection("postgres")]
[Trait("Category", "Postgres")]
public sealed class PostgresCompositionTests
{
    [Fact]
    public async Task RecursiveQueryKeepsRepeatedOccurrencesAndMultipliesQuantitiesInOneStatement()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var part = Part(1, 2m);
        var bolt = new PDMObject(ObjectType.StandardPart, null, "Болт", massKg: 0.5m);
        var sub = Assembly(2, new(part, 4), new(bolt, 2));
        var root = Assembly(3, new(sub, 3), new(part, 2));
        await Save(test, root);
        var counter = new QueryCounter();
        var reader = Reader(test, counter);
        var entries = await reader.GetAsync(root.Id);
        Assert.Contains("WITH RECURSIVE", Assert.Single(counter.Commands));
        Assert.Equal(5, entries.Count);
        var occurrences = entries.Where(entry => entry.ObjectId == part.Id).ToArray();
        Assert.Equal(2, occurrences.Length);
        Assert.Contains(occurrences, entry => entry.Path.SequenceEqual(new[] { root.Id, part.Id }) && entry.TotalQuantity == 2);
        Assert.Contains(occurrences, entry => entry.Path.SequenceEqual(new[] { root.Id, sub.Id, part.Id }) && entry.TotalQuantity == 12);
        counter.Commands.Clear();
        var result = await new AssemblyCalculationService(reader).CalculateAsync(root.Id);
        Assert.Single(counter.Commands);
        Assert.Equal(31m, result.MassKg);
        Assert.Equal(14m, Assert.Single(result.Specification, entry => entry.ObjectId == part.Id).Quantity);
        Assert.Equal(6m, Assert.Single(result.Specification, entry => entry.ObjectId == bolt.Id).Quantity);
    }

    [Fact]
    public async Task QueryUsesLatestNonAnnulledChildVersion()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var part = Part(1, 2m);
        part.CurrentVersion!.Approve();
        var current = part.CreateNewVersion();
        current.Update("Деталь v2", "Сталь", 3m, []);
        current.Approve();
        var annulled = part.CreateNewVersion();
        annulled.Update("Деталь v3", "Сталь", 4m, []);
        annulled.Annul();
        var root = Assembly(2, new Component(part, 2));
        await Save(test, root);
        var reader = Reader(test);
        var row = Assert.Single(await reader.GetAsync(root.Id), entry => entry.ObjectId == part.Id);
        Assert.Equal(current.Id, row.CurrentVersionId);
        Assert.Equal(2, row.CurrentVersionNumber);
        Assert.Equal("Деталь v2", row.Name);
        Assert.Equal(3m, row.MassKg);
        Assert.Equal(6m, (await new AssemblyCalculationService(reader).CalculateAsync(root.Id)).MassKg);
    }

    [Fact]
    public async Task ChildWithoutCurrentVersionRemainsVisibleAndPreventsPartialCalculation()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var part = Part(1, 2m);
        part.CurrentVersion!.Annul();
        var root = Assembly(2, new Component(part, 2));
        await Save(test, root);
        var reader = Reader(test);
        var row = Assert.Single(await reader.GetAsync(root.Id), entry => entry.ObjectId == part.Id);
        Assert.Null(row.CurrentVersionId);
        Assert.Equal(part.Name, row.Name);
        Assert.Null(row.MassKg);
        var result = await new AssemblyCalculationService(reader).CalculateAsync(root.Id);
        Assert.Null(result.MassKg);
        Assert.Empty(result.Specification);
        Assert.Equal(CalculationIssueKind.NoCurrentVersion, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task QueryStopsAtCycleAndReturnsClosingPath()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var first = Assembly(1);
        var second = Assembly(2, new Component(first, 1));
        first.CurrentVersion!.Update(first.Name, null, null, [new(second, 1)]);
        await Save(test, first);
        var reader = Reader(test);
        var entries = await reader.GetAsync(first.Id);
        Assert.Equal(3, entries.Count);
        var cycle = Assert.Single(entries, entry => entry.IsCycle);
        Assert.Equal(new[] { first.Id, second.Id, first.Id }, cycle.Path);
        var result = await new AssemblyCalculationService(reader).CalculateAsync(first.Id);
        Assert.Null(result.MassKg);
        Assert.Equal(CalculationIssueKind.Cycle, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task PathQuantityOverflowIsReturnedAsDiagnosticInsteadOfNumericConversionFailure()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var leaf = Part(5, 1m);
        var root = leaf;
        for (int i = 4; i >= 1; i--) root = Assembly(i, new Component(root, int.MaxValue));
        await Save(test, root);
        var reader = Reader(test);
        var row = Assert.Single(await reader.GetAsync(root.Id), entry => entry.ObjectId == leaf.Id);
        Assert.Null(row.TotalQuantity);
        var result = await new AssemblyCalculationService(reader).CalculateAsync(root.Id);
        Assert.Null(result.MassKg);
        Assert.Equal(CalculationIssueKind.QuantityOverflow, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task EmptyAssemblyAndMissingRootAreDistinguished()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var root = Assembly(1);
        await Save(test, root);
        var reader = Reader(test);
        Assert.Empty(await reader.GetAsync(Guid.NewGuid()));
        var result = await new AssemblyCalculationService(reader).CalculateAsync(root.Id);
        Assert.Equal(0m, result.MassKg);
        Assert.True(result.IsSuccessful);
        Assert.Empty(result.Specification);
    }

    [Fact]
    public async Task AnnulledRootDoesNotUseItsHistoricalComposition()
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var root = Assembly(1, new Component(Part(2, 2m), 3));
        root.CurrentVersion!.Annul();
        await Save(test, root);
        var row = Assert.Single(await Reader(test).GetAsync(root.Id));
        Assert.Null(row.CurrentVersionId);
        Assert.Equal(root.Name, row.Name);
    }

    [Fact]
    public async Task NameMigrationPreservesExistingDataAndAllowsLongNameOnReimport()
    {
        await using var test = await PostgresTestDatabase.CreateAsync(migrate: false);
        await using (var context = new PdmDbContext(test.Options))
            await context.GetService<IMigrator>().MigrateAsync("20261012133448_EnforceStandardNamesAndPreserveMassPrecision");
        var part = Part(1, 2m);
        await Save(test, part);
        await test.Database.InitializeAsync();
        var name = new string('А', 1000);
        var documents = new CadDocument[] {
            new(1, "part.m3d", CadDocumentType.Part, part.Designation!.Value, name, "Сталь", 2m, []),
            new(1, "other.m3d", CadDocumentType.Part, "АБВГ.123456.002", "Независимая деталь", "Сталь", 1m, []) };
        var report = await new ImportCadFolderService(new DocumentsReader(documents), new CadDomainMapper(), test.Database)
            .ExecuteAsync("искусственная папка");
        Assert.Equal(2, report.Accepted.Count);
        Assert.Empty(report.Rejected);
        await using var read = test.Database.CreateUnitOfWork();
        await read.BeginAsync();
        Assert.Equal(name, (await read.Objects.GetByIdAsync(part.Id))!.Name);
        Assert.Equal(2, (await read.Objects.SearchAsync(string.Empty)).Count);
        await read.RollbackAsync();
    }

    private static PDMObject Part(int number, decimal mass) => new(ObjectType.Part,
        Designation.Create($"АБВГ.123456.{number:000}"), "Деталь", "Сталь", mass);
    private static PDMObject Assembly(int number, params Component[] components) => new(ObjectType.Assembly,
        Designation.Create($"АБВГ.123456.{number:000}"), "Сборка", components: components);
    private static PostgresCompositionReader Reader(PostgresTestDatabase test, QueryCounter? counter = null)
    {
        var options = new DbContextOptionsBuilder<PdmDbContext>(test.Options);
        if (counter is not null) options.AddInterceptors(counter);
        return new PostgresCompositionReader(options.Options);
    }
    private static async Task Save(PostgresTestDatabase test, PDMObject obj)
    {
        await using var work = test.Database.CreateUnitOfWork();
        await work.BeginAsync();
        work.Objects.Add(obj);
        await work.CommitAsync();
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DocumentsReader(IReadOnlyList<CadDocument> documents) : ICadFolderDocumentReader
    {
        public Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default) =>
            Task.FromResult(new CadFolderReadResult(documents, []));
    }
}
