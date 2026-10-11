namespace Contracts.CAD.Records
{
    public sealed record ImportAcceptedFile(string FileName, string Action);
    public sealed record ImportRejectedFile(string FileName, string Reason);
    public sealed record ImportWarning(string FileName, string Message);

    public sealed record ImportCadFolderReport
    (
        IReadOnlyList<ImportAcceptedFile> Accepted,
        IReadOnlyList<ImportRejectedFile> Rejected,
        IReadOnlyList<ImportWarning> Warnings
    );
}