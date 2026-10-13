using Contracts.CAD.Records;
using Application.Interfaces.Services;
using Domain.Objects;

namespace Application.Services;

public sealed class CadDomainMapper : ICadDomainMapper
{
    public PDMObject Map(CadDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var designation = document.Designation is null
            ? null
            : Designation.Create(document.Designation);

        return new PDMObject(
            ObjectTypeMapper.ToDomain(document.Type),
            designation,
            document.Name,
            document.Material,
            document.MassKg);
    }

}
