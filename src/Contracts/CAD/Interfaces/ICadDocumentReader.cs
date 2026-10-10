using Contracts.CAD.Records;

namespace Contracts.CAD.Interfaces;

public interface ICadDocumentReader
{
    Task<CadDocument> ReadAsync(string path, CancellationToken ct = default);
}
