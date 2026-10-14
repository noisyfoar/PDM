using Infrastructure.Persistence;
using Domain.Versions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Infrastructure.Tests;

public sealed class MigrationConsistencyTests
{
    private static PdmDbContext CreateContext() => new(new DbContextOptionsBuilder<PdmDbContext>()
        .UseNpgsql("Host=localhost;Port=1;Database=offline;Username=test;Password=test").Options);

    [Fact]
    public void SavedMigrationSnapshotMatchesCurrentModelWithoutConnectingToDatabase()
    {
        using var context = CreateContext();
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void VersionNameHasNoLengthLimitInPersistenceModel()
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(typeof(ObjectVersion))!.FindProperty(nameof(ObjectVersion.Name))!;
        Assert.Null(property.GetMaxLength());
        Assert.Equal("text", property.GetColumnType());
    }

    [Fact]
    public void AllMigrationsCanGeneratePostgresSchemaWithoutConnectingToDatabase()
    {
        using var context = CreateContext();
        var script = context.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE pdm_object", script);
        Assert.Contains("CREATE TABLE object_version", script);
        Assert.Contains("CREATE TABLE bom_link", script);
        Assert.Contains("IX_pdm_object_standard_name", script);
        Assert.Contains("20261012133448_EnforceStandardNamesAndPreserveMassPrecision", script);
        Assert.Contains("ALTER COLUMN \"Name\" TYPE text", script);
    }
}
