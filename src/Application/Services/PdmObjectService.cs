using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Records.Objects;
using Domain.Exceptions;
using Domain.Objects;
using Domain.Versions;

namespace Application.Services;

public sealed class PdmObjectService : IPdmObjectService
{
    private readonly IUnitOfWorkFactory _factory;

    public PdmObjectService(IUnitOfWorkFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        ct.ThrowIfCancellationRequested();
        var query = searchText.Trim();
        if (query.Length == 0) return Task.FromResult<IReadOnlyList<PdmObjectSummary>>([]);

        return ExecuteAsync<IReadOnlyList<PdmObjectSummary>>(async (work, token) =>
        {
            var objects = await work.Objects.SearchAsync(query, token);
            return objects.Select(ToSummary).ToList().AsReadOnly();
        }, saveChanges: false, ct);
    }

    public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default) =>
        ExecuteAsync(async (work, token) => ToDetails(await GetRequiredAsync(work, objectId, token)),
            saveChanges: false, ct);

    public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default) =>
        ChangeStateAsync(objectId, versionId, version => version.Approve(), ct);

    public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default) =>
        ChangeStateAsync(objectId, versionId, version => version.Annul(), ct);

    private Task<PdmObjectDetails> ChangeStateAsync(Guid objectId, Guid versionId,
        Action<ObjectVersion> changeState, CancellationToken ct) =>
        ExecuteAsync(async (work, token) =>
        {
            var obj = await GetRequiredAsync(work, objectId, token);
            var version = obj.Versions.SingleOrDefault(item => item.Id == versionId)
                ?? throw new PDMException($"Версия «{versionId}» не найдена у объекта «{obj.Name}».");
            changeState(version);
            return ToDetails(obj);
        }, saveChanges: true, ct);

    private async Task<T> ExecuteAsync<T>(Func<IUnitOfWork, CancellationToken, Task<T>> operation,
        bool saveChanges, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await using var work = _factory.CreateUnitOfWork();
        await work.BeginAsync(ct);
        T result;
        try
        {
            result = await operation(work, ct);
            ct.ThrowIfCancellationRequested();
            if (saveChanges) await work.CommitAsync(ct);
        }
        catch (Exception operationError)
        {
            try { await work.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Не удалось выполнить операцию с объектом и откатить транзакцию.",
                    operationError, rollbackError);
            }
            throw;
        }

        // Чтение тоже требует транзакции репозитория, но не вызывает SaveChanges.
        if (!saveChanges) await work.RollbackAsync(CancellationToken.None);
        return result;
    }

    private static async Task<PDMObject> GetRequiredAsync(IUnitOfWork work, Guid objectId, CancellationToken ct) =>
        await work.Objects.GetByIdAsync(objectId, ct)
        ?? throw new PDMException($"Объект с идентификатором «{objectId}» не найден.");

    private static PdmObjectSummary ToSummary(PDMObject obj)
    {
        var current = obj.CurrentVersion;
        return new(obj.Id, obj.ObjectType, obj.Designation?.Value, obj.Name,
            current?.Id, current?.VersionNumber, current?.VersionType);
    }

    private static PdmObjectDetails ToDetails(PDMObject obj)
    {
        var versions = obj.Versions.OrderByDescending(version => version.VersionNumber)
            .Select(version => new PdmVersionDetails(version.Id, version.VersionNumber, version.VersionType,
                version.Name, version.Material, version.MassKg, version.CreatedAt)).ToList().AsReadOnly();
        var currentId = obj.CurrentVersion?.Id;
        return new(obj.Id, obj.ObjectType, obj.Designation?.Value, obj.Name,
            versions.SingleOrDefault(version => version.Id == currentId), versions);
    }
}
