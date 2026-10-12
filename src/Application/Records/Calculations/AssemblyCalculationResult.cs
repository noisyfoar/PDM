using Domain.Objects;

namespace Application.Records.Calculations;

public sealed record AssemblyCalculationResult(
    Guid AssemblyId,
    decimal? MassKg,
    IReadOnlyList<SpecificationEntry> Specification,
    IReadOnlyList<CalculationIssue> Issues)
{
    public bool IsSuccessful => Issues.Count == 0;
}

public sealed record SpecificationEntry(
    Guid ObjectId,
    ObjectType ObjectType,
    string? Designation,
    string Name,
    string? Material,
    decimal Quantity,
    decimal? UnitMassKg,
    decimal? TotalMassKg);

public sealed record CalculationIssue(
    CalculationIssueKind Kind,
    Guid ObjectId,
    string Name,
    string Message,
    IReadOnlyList<Guid> Path);

public enum CalculationIssueKind
{
    ObjectNotFound,
    NotAssembly,
    NoCurrentVersion,
    Cycle,
    InvalidQuantity,
    QuantityOverflow,
    MissingMass,
    MassOverflow
}
