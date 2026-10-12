using Domain.Objects;
using Domain.Versions;

namespace Application.Records.Composition;

public sealed record CompositionEntry(
    Guid ObjectId,
    Guid? ParentObjectId,
    Guid? CurrentVersionId,
    ObjectType ObjectType,
    string? Designation,
    string Name,
    ObjectVersionType? VersionType,
    string? Material,
    decimal? MassKg,
    int Quantity,
    decimal? TotalQuantity,
    int Depth,
    IReadOnlyList<Guid> Path,
    bool IsCycle);
