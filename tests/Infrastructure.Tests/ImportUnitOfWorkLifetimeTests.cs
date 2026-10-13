using Application.Interfaces.Persistence;
using Application.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Domain.Objects;
using Xunit;

namespace Infrastructure.Tests;

public sealed class ImportUnitOfWorkLifetimeTests
{
    private static CadDocument Part() => new(1, "part.m3d", CadDocumentType.Part,
        "АБВГ.123456.001", "Деталь", "Сталь", 2m, []);
    private static ImportCadFolderService Service(Factory factory, params CadDocument[] documents) =>
        new(new Reader(documents), new CadDomainMapper(), factory);

    [Fact]
    public async Task RepeatedImportUsesAndDisposesSeparateUnitsOfWork()
    {
        var factory = new Factory();
        var service = Service(factory, Part());
        await service.ExecuteAsync("папка");
        var report = await service.ExecuteAsync("папка");
        Assert.Equal("Пропущен: изменений нет.", Assert.Single(report.Accepted).Action);
        Assert.Single(factory.Saved);
        Assert.Equal(2, factory.Works.Count);
        Assert.NotSame(factory.Works[0], factory.Works[1]);
        Assert.All(factory.Works, work =>
        {
            Assert.Equal(1, work.Begins);
            Assert.Equal(1, work.Commits);
            Assert.Equal(0, work.Rollbacks);
            Assert.Equal(1, work.Disposals);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NothingAcceptedDoesNotCreateUnitOfWork(bool invalidDocument)
    {
        var factory = new Factory();
        var documents = invalidDocument ? new[] { Part() with { Material = null } } : [];
        var report = await Service(factory, documents).ExecuteAsync("папка");
        Assert.Empty(report.Accepted);
        Assert.Equal(invalidDocument ? 1 : 0, report.Rejected.Count);
        Assert.Empty(factory.Works);
    }

    [Fact]
    public async Task SavingFailureRollsBackAndDisposesUnitOfWork()
    {
        var original = new InvalidOperationException("Ошибка сохранения.");
        var factory = new Factory { CommitError = original };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(factory, Part()).ExecuteAsync("папка"));
        Assert.Same(original, error);
        Assert.Empty(factory.Saved);
        var work = Assert.Single(factory.Works);
        Assert.Equal(1, work.Rollbacks);
        Assert.Equal(1, work.Disposals);
    }

    [Fact]
    public async Task BeginFailureDisposesUnitOfWorkWithoutRollback()
    {
        var original = new InvalidOperationException("Ошибка начала транзакции.");
        var factory = new Factory { BeginError = original };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(factory, Part()).ExecuteAsync("папка"));
        Assert.Same(original, error);
        var work = Assert.Single(factory.Works);
        Assert.Equal(0, work.Commits);
        Assert.Equal(0, work.Rollbacks);
        Assert.Equal(1, work.Disposals);
    }

    private sealed class Reader(IReadOnlyList<CadDocument> documents) : ICadFolderDocumentReader
    {
        public Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default) =>
            Task.FromResult(new CadFolderReadResult(documents, []));
    }

    private sealed class Factory : IUnitOfWorkFactory
    {
        public Dictionary<Guid, PDMObject> Saved { get; } = [];
        public List<Work> Works { get; } = [];
        public Exception? CommitError { get; init; }
        public Exception? BeginError { get; init; }
        public IUnitOfWork CreateUnitOfWork()
        {
            var work = new Work(this);
            Works.Add(work);
            return work;
        }
    }

    private sealed class Work(Factory factory) : IUnitOfWork, IPdmObjectRepository
    {
        private readonly List<PDMObject> _pending = [];
        private bool _active;
        public IPdmObjectRepository Objects => this;
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public int Disposals { get; private set; }
        public Task BeginAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Begins++;
            if (factory.BeginError is not null) return Task.FromException(factory.BeginError);
            _active = true;
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken ct = default)
        {
            RequireActive();
            ct.ThrowIfCancellationRequested();
            Commits++;
            if (factory.CommitError is not null) return Task.FromException(factory.CommitError);
            foreach (var obj in _pending) factory.Saved.Add(obj.Id, obj);
            _pending.Clear();
            _active = false;
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            RequireActive();
            ct.ThrowIfCancellationRequested();
            Rollbacks++;
            _pending.Clear();
            _active = false;
            return Task.CompletedTask;
        }
        public void Add(PDMObject obj) { RequireActive(); _pending.Add(obj); }
        public Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default)
        {
            RequireActive();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(factory.Saved.Values.SingleOrDefault(obj => obj.Designation?.Value == designation.Value));
        }
        public Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PDMObject>> SearchAsync(string text, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposals++; _pending.Clear(); _active = false; return ValueTask.CompletedTask; }
        private void RequireActive()
        {
            if (!_active || Disposals > 0) throw new InvalidOperationException("Нет действующей транзакции.");
        }
    }
}
