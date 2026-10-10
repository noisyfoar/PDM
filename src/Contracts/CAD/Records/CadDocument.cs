using Contracts.CAD.Enums;

namespace Contracts.CAD.Records
{
    public sealed record CadDocument
    (
        int FormatVersion,
        string FileName,
        CadDocumentType Type,
        string? Designation,
        string Name,
        string? Material,
        decimal? MassKg,
        IReadOnlyList<CadComponent> Components
    );
}
