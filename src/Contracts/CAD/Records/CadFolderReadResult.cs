namespace Contracts.CAD.Records
{
    public sealed record CadReadError(string FileName, string Message);

    public sealed record CadFolderReadResult(IReadOnlyList<CadDocument> Documents, IReadOnlyList<CadReadError> Errors);
}
