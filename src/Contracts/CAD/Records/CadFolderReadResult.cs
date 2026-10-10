namespace Contracts.CAD.Records;

public sealed record CadReadError(string Path, string Message);

public sealed record CadFolderReadResult(IReadOnlyList<CadDocument> Documents, IReadOnlyList<CadReadError> Errors);
