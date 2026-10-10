using Contracts.CAD.Records;

namespace Contracts.CAD.Interfaces;

public interface ICadFolderDocumentReader
{
    Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default);
}
