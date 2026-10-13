using Domain.Objects;

namespace Application.Records.Objects;

public sealed record PdmObjectDetails(
    Guid Id,
    ObjectType ObjectType,
    string? Designation,
    string Name,
    PdmVersionDetails? CurrentVersion,
    IReadOnlyList<PdmVersionDetails> Versions);
