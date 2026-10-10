using Contracts.CAD.Records;
using Domain.Objects;

namespace Application.Interfaces.Services;

public interface ICadDomainMapper
{
    IReadOnlyList<PDMObject> Map(IReadOnlyList<CadDocument> documents);
}
