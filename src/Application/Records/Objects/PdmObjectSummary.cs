using Domain.Objects;
using Domain.Versions;

namespace Application.Records.Objects;

public sealed record PdmObjectSummary(
    Guid Id,
    ObjectType ObjectType,
    string? Designation,
    string Name,
    Guid? CurrentVersionId,
    int? CurrentVersionNumber,
    ObjectVersionType? CurrentVersionType);
