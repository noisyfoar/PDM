namespace Application.Interfaces.Persistence;

public interface IUnitOfWork : IAsyncDisposable
{
    IPdmObjectRepository Objects { get; }

    Task BeginAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
