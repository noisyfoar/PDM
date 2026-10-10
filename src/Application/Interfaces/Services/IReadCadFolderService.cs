using Contracts.CAD.Records;

namespace Application.Interfaces.Services;

public interface IReadCadFolderService
{
    Task<CadFolderReadResult> ExecuteAsync(string folderPath, CancellationToken ct = default);
}
