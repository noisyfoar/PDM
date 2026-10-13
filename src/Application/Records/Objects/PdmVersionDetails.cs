using Domain.Versions;

namespace Application.Records.Objects;

public sealed record PdmVersionDetails(
    Guid Id,
    int VersionNumber,
    ObjectVersionType VersionType,
    string Name,
    string? Material,
    decimal? MassKg,
    DateTime CreatedAt);
