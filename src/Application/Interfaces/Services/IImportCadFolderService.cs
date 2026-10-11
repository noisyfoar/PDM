using Contracts.CAD.Records;

namespace Application.Interfaces.Services;

public interface IImportCadFolderService
{
    Task<ImportCadFolderReport> ExecuteAsync(string folderPath, CancellationToken ct = default);
}
