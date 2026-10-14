using Application.Services;
using Domain.Objects;
using Domain.Versions;
using Infrastructure.Cad;
using Xunit;

namespace Infrastructure.Tests;

[Collection("postgres")]
[Trait("Category", "OriginalCadImport")]
public sealed class OriginalCadImportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothSourceDatasetsRespectPartialImportCalculationsAndVersionPolicy(bool approveBeforeReimport)
    {
        await using var test = await PostgresTestDatabase.CreateAsync();
        var importer = new ImportCadFolderService(new CadFolderDocumentReader(new JsonCadDocumentReader()), new CadDomainMapper(), test.Database);
        var first = await importer.ExecuteAsync(CadExportFolders.Find("cad-export"));
        Assert.Equal(35, first.Accepted.Count);
        Assert.Equal(10, first.Rejected.Count);
        Assert.Equal("Прокладка маслоуказателя.m3d", Assert.Single(first.Warnings).FileName);
        Assert.Contains(first.Rejected, row => row.FileName == "Шайба стопорная.m3d" && row.Reason.Contains("Дублируется"));
        Assert.Contains(first.Rejected, row => row.FileName == "Шайба упорная.m3d" && row.Reason.Contains("Дублируется"));
        Assert.Contains(first.Rejected, row => row.FileName == "Механизм переключения.a3d" && row.Reason.Contains("отклонён"));
        Assert.Contains(first.Rejected, row => row.FileName == "Отдушина.m3d" && row.Reason.Contains("JSON"));
        var before = await Load(test);
        Assert.Equal(35, before.Count);
        var root = Assert.Single(before, obj => obj.Designation?.Value == "РДЦЛ.304112.000");
        var gear = Assert.Single(before, obj => obj.Name == "Колесо зубчатое");
        var cover = Assert.Single(before, obj => obj.Name == "Крышка подшипника в сборе");
        var calculator = new AssemblyCalculationService(test.Database.CreateCompositionReader());
        var initialMass = await calculator.CalculateAsync(root.Id);
        Assert.Equal(98.8040m, initialMass.MassKg);
        Assert.Equal(25, initialMass.Specification.Count);
        var missingMassAssembly = Assert.Single(before, obj => obj.Name == "Маслоуказатель в сборе");
        var incomplete = await calculator.CalculateAsync(missingMassAssembly.Id);
        Assert.Null(incomplete.MassKg);
        Assert.Equal(3, incomplete.Specification.Count);
        Assert.Equal("Прокладка маслоуказателя", Assert.Single(incomplete.Issues).Name);
        Assert.Null(Assert.Single(incomplete.Specification, row => row.Name == "Прокладка маслоуказателя").TotalMassKg);

        if (approveBeforeReimport)
        {
            var service = new PdmObjectService(test.Database);
            foreach (var obj in before) await service.ApproveAsync(obj.Id, obj.CurrentVersion!.Id);
        }
        var second = await importer.ExecuteAsync(CadExportFolders.Find("cad-export-v2"));
        Assert.Equal(35, second.Accepted.Count);
        Assert.Equal(10, second.Rejected.Count);
        Assert.Single(second.Warnings);
        Assert.Equal(33, second.Accepted.Count(row => row.Action == "Пропущен: изменений нет."));
        var changedAction = approveBeforeReimport ? "Создана новая рабочая версия." : "Обновлена рабочая версия.";
        Assert.Equal(2, second.Accepted.Count(row => row.Action == changedAction));
        var after = await Load(test);
        Assert.Equal(before.Select(obj => obj.Id).Order(), after.Select(obj => obj.Id).Order());
        var newGear = after.Single(obj => obj.Id == gear.Id);
        var newCover = after.Single(obj => obj.Id == cover.Id);
        var expectedVersion = approveBeforeReimport ? 2 : 1;
        Assert.Equal(expectedVersion, newGear.CurrentVersion!.VersionNumber);
        Assert.Equal(expectedVersion, newCover.CurrentVersion!.VersionNumber);
        Assert.Equal(5.92m, newGear.CurrentVersion.MassKg);
        Assert.Equal(4, newCover.CurrentVersion.Components.Single(link => link.ChildObject.Name == "Прокладка регулировочная").Quantity);
        Assert.Equal(ObjectVersionType.InWork, newGear.CurrentVersion.VersionType);
        if (approveBeforeReimport)
        {
            Assert.Equal(5.86m, newGear.Versions[0].MassKg);
            Assert.Equal(3, newCover.Versions[0].Components.Single(link => link.ChildObject.Name == "Прокладка регулировочная").Quantity);
            Assert.Equal(ObjectVersionType.Approved, newGear.Versions[0].VersionType);
        }
        else
        {
            Assert.Equal(gear.CurrentVersion!.Id, newGear.CurrentVersion.Id);
            Assert.Equal(cover.CurrentVersion!.Id, newCover.CurrentVersion.Id);
        }
        var newRoot = after.Single(obj => obj.Id == root.Id);
        Assert.Equal(root.CurrentVersion!.Id, newRoot.CurrentVersion!.Id);
        Assert.Single(newRoot.Versions);
        Assert.Equal(98.8800m, (await calculator.CalculateAsync(root.Id)).MassKg);

        var repeated = await importer.ExecuteAsync(CadExportFolders.Find("cad-export-v2"));
        Assert.Equal(35, repeated.Accepted.Count);
        Assert.All(repeated.Accepted, row => Assert.Equal("Пропущен: изменений нет.", row.Action));
        var unchanged = await Load(test);
        Assert.All(unchanged, obj =>
        {
            var previous = after.Single(item => item.Id == obj.Id);
            Assert.Equal(previous.CurrentVersion!.Id, obj.CurrentVersion!.Id);
            Assert.Equal(previous.Versions.Count, obj.Versions.Count);
        });
    }

    private static async Task<IReadOnlyList<PDMObject>> Load(PostgresTestDatabase test)
    {
        await using var work = test.Database.CreateUnitOfWork();
        await work.BeginAsync();
        var objects = await work.Objects.SearchAsync(string.Empty);
        await work.RollbackAsync();
        return objects;
    }
}
