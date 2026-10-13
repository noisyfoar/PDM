using Application.Records.Objects;

namespace Application.Interfaces.Services;

public interface IPdmObjectService
{
    // Пустая строка возвращает пустой список без обращения к хранилищу.
    Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default);
    Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default);
    Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default);
    Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default);
}
