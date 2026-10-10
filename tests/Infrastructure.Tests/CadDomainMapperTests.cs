using Application.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Records;
using Domain.Objects;
using Xunit;

namespace Infrastructure.Tests;

public sealed class CadDomainMapperTests
{
    [Fact]
    public void MapsAssemblyAndResolvesComponentByFileName()
    {
        var documents = new CadDocument[]
        {
            new(1, "part.m3d", CadDocumentType.Part, "АБВГ.123456.001", "Деталь", "Сталь", 2m, []),
            new(1, "assembly.a3d", CadDocumentType.Assembly, "АБВГ.123456.002", "Сборка", null, null,
                [new CadComponent("part.m3d", 3)])
        };

        var result = new CadDomainMapper().Map(documents);
        var assembly = Assert.Single(result.Where(x => x.ObjectType == ObjectType.Assembly));

        var component = Assert.Single(assembly.CurrentVersion!.Components);
        Assert.Equal(3, component.Quantity);
        Assert.Equal("Деталь", component.ChildObject.Name);
    }

    [Fact]
    public void MissingComponentIsRejected()
    {
        var document = new CadDocument(1, "assembly.a3d", CadDocumentType.Assembly,
            "АБВГ.123456.002", "Сборка", null, null,
            [new CadComponent("missing.m3d", 1)]);

        Assert.Throws<Domain.Exceptions.PDMException>(() => new CadDomainMapper().Map([document]));
    }
}
