using Application.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Records;
using Domain.Objects;
using Xunit;

namespace Infrastructure.Tests;

public sealed class CadDomainMapperTests
{
    [Fact]
    public void MapsPartAttributes()
    {
        var document = new CadDocument(1, "part.m3d", CadDocumentType.Part,
            "АБВГ.123456.001", "Деталь", "Сталь", 2m, []);
        var result = new CadDomainMapper().Map(document);
        Assert.Equal(ObjectType.Part, result.ObjectType);
        Assert.Equal(document.Designation, result.Designation!.Value);
        Assert.Equal(document.Name, result.Name);
        Assert.Equal(document.Material, result.CurrentVersion!.Material);
        Assert.Equal(document.MassKg, result.CurrentVersion.MassKg);
    }

    [Fact]
    public void AssemblyCompositionIsResolvedByImportService()
    {
        var document = new CadDocument(1, "assembly.a3d", CadDocumentType.Assembly,
            "АБВГ.123456.002", "Сборка", null, null,
            [new CadComponent("missing.m3d", 1)]);

        var result = new CadDomainMapper().Map(document);
        Assert.Equal(ObjectType.Assembly, result.ObjectType);
        Assert.Empty(result.CurrentVersion!.Components);
    }
}
