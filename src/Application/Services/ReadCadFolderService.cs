using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Application.Interfaces.Services;

namespace Application.Services;

public sealed class ReadCadFolderService : IReadCadFolderService
{
    private readonly ICadFolderDocumentReader _reader;

    public ReadCadFolderService(ICadFolderDocumentReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    public Task<CadFolderReadResult> ExecuteAsync(string folderPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        return _reader.ReadAsync(folderPath, ct);
    }
}
