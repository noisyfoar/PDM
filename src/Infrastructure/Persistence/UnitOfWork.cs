using System.Data;
using Application.Interfaces.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly PdmDbContext _context;
    private IDbContextTransaction? _transaction;
    private bool _disposed;
    public IPdmObjectRepository Objects { get; }

    internal UnitOfWork(PdmDbContext context)
    {
        _context = context;
        Objects = new PdmObjectRepository(context, this);
    }

    public async Task BeginAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transaction is not null) throw new InvalidOperationException("Транзакция уже начата.");
        _context.ChangeTracker.Clear();
        _transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    }

    internal void RequireTransaction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_transaction is null) throw new InvalidOperationException("Сначала необходимо начать транзакцию.");
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        RequireTransaction();
        await _context.SaveChangesAsync(ct);
        await _transaction!.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        _context.ChangeTracker.Clear();
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        RequireTransaction();
        await _transaction!.RollbackAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
        _context.ChangeTracker.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (_transaction is not null) await _transaction.DisposeAsync(); }
        finally { await _context.DisposeAsync(); }
    }
}
