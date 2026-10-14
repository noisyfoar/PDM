using Application.Interfaces.Persistence;
using Application.Records.Objects;
using Application.Services;
using Domain.Exceptions;
using Domain.Objects;
using Domain.Versions;
using Xunit;

namespace Infrastructure.Tests;

public sealed class PdmObjectServiceTests
{
    private static PDMObject Part(int number = 1, string name = "Деталь") =>
        new(ObjectType.Part, Designation.Create($"АБВГ.123456.{number:000}"), name, "Сталь", 2m);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task EmptySearchReturnsAllObjectsAndCompletesReadTransaction(string query)
    {
        var part = Part();
        var standard = new PDMObject(ObjectType.StandardPart, null, "Болт", massKg: 0.1m);
        var factory = new Factory(part, standard);
        var result = await new PdmObjectService(factory).SearchAsync(query);
        Assert.Equal(new[] { part.Id, standard.Id }.Order(), result.Select(obj => obj.Id).Order());
        Assert.Equal(string.Empty, factory.LastSearch);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Theory]
    [InlineData("  ДЕТАЛЬ  ", "ДЕТАЛЬ")]
    [InlineData("АБВГ.123456.001", "АБВГ.123456.001")]
    public async Task SearchUsesNameOrDesignationAndReturnsSummary(string query, string expectedQuery)
    {
        var source = Part();
        var factory = new Factory(source, Part(2, "Другая вещь"));
        var result = await new PdmObjectService(factory).SearchAsync(query);
        var item = Assert.Single(result);
        Assert.Equal(source.Id, item.Id);
        Assert.Equal(source.Designation!.Value, item.Designation);
        Assert.Equal(ObjectType.Part, item.ObjectType);
        Assert.Equal(source.Name, item.Name);
        Assert.Equal(source.CurrentVersion!.Id, item.CurrentVersionId);
        Assert.Equal(1, item.CurrentVersionNumber);
        Assert.Equal(ObjectVersionType.InWork, item.CurrentVersionType);
        Assert.Equal(expectedQuery, factory.LastSearch);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task SearchWithoutMatchesReturnsEmptyList()
    {
        var factory = new Factory(Part());
        Assert.Empty(await new PdmObjectService(factory).SearchAsync("Несуществующий объект"));
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task GetReturnsCurrentAttributesAndHistoryAsDetachedSnapshots()
    {
        var source = Part();
        source.CurrentVersion!.Approve();
        var latest = source.CreateNewVersion();
        latest.Update("Деталь v2", "Алюминий", 3m, []);
        var factory = new Factory(source);
        var details = await new PdmObjectService(factory).GetAsync(source.Id);
        Assert.Equal(source.Id, details.Id);
        Assert.Equal(source.Designation!.Value, details.Designation);
        Assert.Equal(ObjectType.Part, details.ObjectType);
        Assert.Equal("Деталь v2", details.Name);
        Assert.Equal(new[] { 2, 1 }, details.Versions.Select(version => version.VersionNumber));
        var current = details.CurrentVersion!;
        Assert.Same(details.Versions[0], current);
        Assert.Equal(latest.Id, current.Id);
        Assert.Equal(latest.CreatedAt, current.CreatedAt);
        Assert.Equal("Алюминий", current.Material);
        Assert.Equal(3m, current.MassKg);
        Assert.Equal(ObjectVersionType.Approved, details.Versions[1].VersionType);
        Assert.Equal("Сталь", details.Versions[1].Material);
        latest.Update("Изменена после чтения", "Латунь", 5m, []);
        latest.Annul();
        Assert.Equal("Деталь v2", details.Name);
        Assert.Equal("Алюминий", current.Material);
        Assert.Equal(3m, current.MassKg);
        Assert.Equal(ObjectVersionType.InWork, current.VersionType);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task StandardPartHasNoDesignationOrMaterialInCard()
    {
        var source = new PDMObject(ObjectType.StandardPart, null, "Болт", massKg: 0.1m);
        var details = await new PdmObjectService(new Factory(source)).GetAsync(source.Id);
        Assert.Null(details.Designation);
        Assert.Null(details.CurrentVersion!.Material);
        Assert.Equal(0.1m, details.CurrentVersion.MassKg);
    }

    [Fact]
    public async Task AllAnnulledVersionsHaveHistoryAndNameButNoCurrentVersion()
    {
        var source = Part();
        var first = source.CurrentVersion!;
        first.Approve();
        var latest = source.CreateNewVersion();
        latest.Update("Деталь v2", "Сталь", 3m, []);
        latest.Annul();
        first.Annul();
        var service = new PdmObjectService(new Factory(source));
        var details = await service.GetAsync(source.Id);
        var summary = Assert.Single(await service.SearchAsync("Деталь"));
        Assert.Null(details.CurrentVersion);
        Assert.Equal("Деталь v2", details.Name);
        Assert.Equal(2, details.Versions.Count);
        Assert.All(details.Versions, version => Assert.Equal(ObjectVersionType.Annulled, version.VersionType));
        Assert.Null(summary.CurrentVersionId);
        Assert.Null(summary.CurrentVersionNumber);
        Assert.Null(summary.CurrentVersionType);
        Assert.Equal("Деталь v2", summary.Name);
    }

    [Theory]
    [InlineData("get")]
    [InlineData("approve")]
    [InlineData("annul")]
    public async Task MissingObjectReturnsFriendlyErrorAndRollsBack(string operation)
    {
        var factory = new Factory();
        var objectId = Guid.NewGuid();
        var service = new PdmObjectService(factory);
        var error = await Assert.ThrowsAsync<PDMException>(() => Invoke(service, operation, objectId, Guid.NewGuid()));
        Assert.Contains(objectId.ToString(), error.Message);
        Assert.Contains("не найден", error.Message);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task ApproveCommitsAndReturnsUpdatedCard()
    {
        var source = Part();
        var versionId = source.CurrentVersion!.Id;
        var factory = new Factory(source);
        var details = await new PdmObjectService(factory).ApproveAsync(source.Id, versionId);
        Assert.Equal(ObjectVersionType.Approved, source.CurrentVersion.VersionType);
        Assert.Equal(ObjectVersionType.Approved, details.CurrentVersion!.VersionType);
        Assert.Equal(versionId, details.CurrentVersion.Id);
        Assert.Single(details.Versions);
        AssertChangeCompleted(Assert.Single(factory.Works));
    }

    [Theory]
    [InlineData(ObjectVersionType.InWork)]
    [InlineData(ObjectVersionType.Approved)]
    public async Task AnnulLastCurrentVersionReturnsCardWithoutCurrentVersion(ObjectVersionType initialState)
    {
        var source = Part();
        if (initialState == ObjectVersionType.Approved) source.CurrentVersion!.Approve();
        var factory = new Factory(source);
        var details = await new PdmObjectService(factory).AnnulAsync(source.Id, source.CurrentVersion!.Id);
        Assert.Null(source.CurrentVersion);
        Assert.Null(details.CurrentVersion);
        Assert.Equal("Деталь", details.Name);
        Assert.Equal(ObjectVersionType.Annulled, Assert.Single(details.Versions).VersionType);
        AssertChangeCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task AnnulLatestRestoresPreviousCurrentAttributes()
    {
        var source = Part();
        var first = source.CurrentVersion!;
        first.Approve();
        var latest = source.CreateNewVersion();
        latest.Update("Деталь v2", "Алюминий", 7m, []);
        var details = await new PdmObjectService(new Factory(source)).AnnulAsync(source.Id, latest.Id);
        Assert.Equal(first.Id, details.CurrentVersion!.Id);
        Assert.Equal("Деталь", details.Name);
        Assert.Equal("Сталь", details.CurrentVersion.Material);
        Assert.Equal(2m, details.CurrentVersion.MassKg);
        Assert.Equal(ObjectVersionType.Approved, details.CurrentVersion.VersionType);
        Assert.Equal(ObjectVersionType.Annulled, details.Versions[0].VersionType);
        Assert.Equal(2, details.Versions.Count);
    }

    [Fact]
    public async Task AnnulHistoricalVersionDoesNotAnnulCurrentVersion()
    {
        var source = Part();
        var first = source.CurrentVersion!;
        first.Approve();
        var latest = source.CreateNewVersion();
        var details = await new PdmObjectService(new Factory(source)).AnnulAsync(source.Id, first.Id);
        Assert.Equal(latest.Id, details.CurrentVersion!.Id);
        Assert.Equal(ObjectVersionType.InWork, details.CurrentVersion.VersionType);
        Assert.Equal(ObjectVersionType.Annulled, details.Versions[1].VersionType);
    }

    [Theory]
    [InlineData("approve", ObjectVersionType.Approved)]
    [InlineData("approve", ObjectVersionType.Annulled)]
    [InlineData("annul", ObjectVersionType.Annulled)]
    public async Task ForbiddenTransitionsAreRejectedByDomainWithoutCommit(string operation, ObjectVersionType state)
    {
        var source = Part();
        var version = source.CurrentVersion!;
        if (state == ObjectVersionType.Approved) version.Approve();
        if (state == ObjectVersionType.Annulled) version.Annul();
        var factory = new Factory(source);
        await Assert.ThrowsAsync<PDMException>(() => Invoke(new PdmObjectService(factory), operation, source.Id, version.Id));
        Assert.Equal(state, version.VersionType);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionMustBelongToRequestedObject(bool foreignVersion)
    {
        var source = Part();
        var other = Part(2, "Другой объект");
        var versionId = foreignVersion ? other.CurrentVersion!.Id : Guid.NewGuid();
        var factory = new Factory(source, other);
        var error = await Assert.ThrowsAsync<PDMException>(() =>
            new PdmObjectService(factory).ApproveAsync(source.Id, versionId));
        Assert.Contains(versionId.ToString(), error.Message);
        Assert.Contains(source.Name, error.Message);
        Assert.Equal(ObjectVersionType.InWork, source.CurrentVersion!.VersionType);
        Assert.Equal(ObjectVersionType.InWork, other.CurrentVersion!.VersionType);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task EachOperationUsesNewUnitOfWorkAndOldResultsRemainSnapshots()
    {
        var source = Part();
        var factory = new Factory(source);
        var service = new PdmObjectService(factory);
        var oldDetails = await service.GetAsync(source.Id);
        await service.ApproveAsync(source.Id, source.CurrentVersion!.Id);
        var summary = Assert.Single(await service.SearchAsync("Деталь"));
        Assert.Equal(ObjectVersionType.InWork, oldDetails.CurrentVersion!.VersionType);
        Assert.Equal(ObjectVersionType.Approved, summary.CurrentVersionType);
        Assert.Equal(3, factory.Works.Distinct().Count());
        Assert.All(factory.Works, work => Assert.Equal(1, work.Disposals));
        AssertReadCompleted(factory.Works[0]);
        AssertChangeCompleted(factory.Works[1]);
        AssertReadCompleted(factory.Works[2]);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("get")]
    public async Task RepositoryErrorIsPreservedAndTransactionIsRolledBack(string operation)
    {
        var source = Part();
        var original = new InvalidOperationException("Ошибка чтения.");
        var factory = new Factory(source) { ReadError = original };
        var service = new PdmObjectService(factory);
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (operation == "search") await service.SearchAsync("Деталь");
            else await service.GetAsync(source.Id);
        });
        Assert.Same(original, actual);
        AssertReadCompleted(Assert.Single(factory.Works));
    }

    [Fact]
    public async Task CommitErrorRollsBackChangedStateAndPreservesOriginalException()
    {
        var source = Part();
        var original = new InvalidOperationException("Ошибка сохранения.");
        var factory = new Factory(source) { CommitError = original };
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PdmObjectService(factory).ApproveAsync(source.Id, source.CurrentVersion!.Id));
        Assert.Same(original, actual);
        Assert.Equal(ObjectVersionType.InWork, source.CurrentVersion!.VersionType);
        var work = Assert.Single(factory.Works);
        Assert.Equal(1, work.Commits);
        Assert.Equal(1, work.Rollbacks);
        Assert.Equal(1, work.Disposals);
        Assert.False(work.RollbackToken.CanBeCanceled);
    }

    [Fact]
    public async Task RollbackErrorPreservesBothFailures()
    {
        var source = Part();
        var original = new InvalidOperationException("Ошибка сохранения.");
        var rollback = new InvalidOperationException("Ошибка отката.");
        var factory = new Factory(source) { CommitError = original, RollbackError = rollback };
        var error = await Assert.ThrowsAsync<AggregateException>(() =>
            new PdmObjectService(factory).ApproveAsync(source.Id, source.CurrentVersion!.Id));
        Assert.Equal(new[] { original, rollback }, error.InnerExceptions);
        Assert.Equal(1, Assert.Single(factory.Works).Disposals);
    }

    [Fact]
    public async Task BeginErrorDisposesUnitOfWorkWithoutAttemptingRollback()
    {
        var source = Part();
        var original = new InvalidOperationException("Не удалось начать транзакцию.");
        var factory = new Factory(source) { BeginError = original };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PdmObjectService(factory).GetAsync(source.Id));
        Assert.Same(original, error);
        var work = Assert.Single(factory.Works);
        Assert.Equal(0, work.Rollbacks);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, work.Disposals);
    }

    [Fact]
    public async Task ReadTransactionClosingErrorIsNotRetriedOrHidden()
    {
        var source = Part();
        var original = new InvalidOperationException("Ошибка завершения чтения.");
        var factory = new Factory(source) { RollbackError = original };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new PdmObjectService(factory).GetAsync(source.Id));
        Assert.Same(original, error);
        Assert.Equal(1, Assert.Single(factory.Works).Rollbacks);
        Assert.Equal(1, factory.Works[0].Disposals);
    }

    [Theory]
    [InlineData("search")]
    [InlineData("get")]
    [InlineData("approve")]
    [InlineData("annul")]
    public async Task PreCancelledOperationDoesNotCreateUnitOfWork(string operation)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var factory = new Factory();
        var service = new PdmObjectService(factory);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (operation == "search") await service.SearchAsync("", cancellation.Token);
            else await Invoke(service, operation, Guid.NewGuid(), Guid.NewGuid(), cancellation.Token);
        });
        Assert.Empty(factory.Works);
    }

    [Fact]
    public async Task CancellationAtCommitRollsBackWithUncancelledToken()
    {
        using var cancellation = new CancellationTokenSource();
        var source = Part();
        var factory = new Factory(source) { OnCommit = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PdmObjectService(factory).ApproveAsync(source.Id, source.CurrentVersion!.Id, cancellation.Token));
        Assert.Equal(ObjectVersionType.InWork, source.CurrentVersion!.VersionType);
        var work = Assert.Single(factory.Works);
        Assert.Equal(1, work.Rollbacks);
        Assert.False(work.RollbackToken.CanBeCanceled);
        Assert.Equal(1, work.Disposals);
    }

    private static Task<PdmObjectDetails> Invoke(PdmObjectService service, string operation,
        Guid objectId, Guid versionId, CancellationToken ct = default) => operation switch
    {
        "get" => service.GetAsync(objectId, ct),
        "approve" => service.ApproveAsync(objectId, versionId, ct),
        "annul" => service.AnnulAsync(objectId, versionId, ct),
        _ => throw new ArgumentException(operation)
    };

    private static void AssertReadCompleted(Work work)
    {
        Assert.Equal(1, work.Begins);
        Assert.Equal(0, work.Commits);
        Assert.Equal(1, work.Rollbacks);
        Assert.Equal(1, work.Disposals);
    }

    private static void AssertChangeCompleted(Work work)
    {
        Assert.Equal(1, work.Begins);
        Assert.Equal(1, work.Commits);
        Assert.Equal(0, work.Rollbacks);
        Assert.Equal(1, work.Disposals);
    }

    private sealed class Factory(params PDMObject[] initial) : IUnitOfWorkFactory
    {
        public Dictionary<Guid, PDMObject> Objects { get; } = initial.ToDictionary(obj => obj.Id);
        public List<Work> Works { get; } = [];
        public string? LastSearch { get; set; }
        public Exception? BeginError { get; init; }
        public Exception? ReadError { get; init; }
        public Exception? CommitError { get; init; }
        public Exception? RollbackError { get; init; }
        public Action? OnCommit { get; init; }
        public IUnitOfWork CreateUnitOfWork()
        {
            var work = new Work(this);
            Works.Add(work);
            return work;
        }
    }

    private sealed class Work(Factory factory) : IUnitOfWork, IPdmObjectRepository
    {
        private Dictionary<ObjectVersion, ObjectVersionType> _snapshot = [];
        private bool _active;
        public IPdmObjectRepository Objects => this;
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public int Disposals { get; private set; }
        public CancellationToken RollbackToken { get; private set; }
        public Task BeginAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Begins++;
            if (factory.BeginError is not null) return Task.FromException(factory.BeginError);
            _snapshot = factory.Objects.Values.SelectMany(obj => obj.Versions).ToDictionary(version => version, version => version.VersionType);
            _active = true;
            return Task.CompletedTask;
        }
        public Task CommitAsync(CancellationToken ct = default)
        {
            RequireActive();
            Commits++;
            factory.OnCommit?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (factory.CommitError is not null) return Task.FromException(factory.CommitError);
            _active = false;
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            RequireActive();
            Rollbacks++;
            RollbackToken = ct;
            ct.ThrowIfCancellationRequested();
            if (factory.RollbackError is not null) return Task.FromException(factory.RollbackError);
            RestoreSnapshot();
            return Task.CompletedTask;
        }
        public Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            RequireActive();
            ct.ThrowIfCancellationRequested();
            return factory.ReadError is null ? Task.FromResult(factory.Objects.GetValueOrDefault(id))
                : Task.FromException<PDMObject?>(factory.ReadError);
        }
        public Task<IReadOnlyList<PDMObject>> SearchAsync(string text, CancellationToken ct = default)
        {
            RequireActive();
            ct.ThrowIfCancellationRequested();
            factory.LastSearch = text;
            if (factory.ReadError is not null) return Task.FromException<IReadOnlyList<PDMObject>>(factory.ReadError);
            return Task.FromResult<IReadOnlyList<PDMObject>>(factory.Objects.Values.Where(obj =>
                obj.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || obj.Designation?.Value.Contains(text, StringComparison.OrdinalIgnoreCase) == true).ToArray());
        }
        public void Add(PDMObject obj) => throw new NotSupportedException();
        public Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (_active) RestoreSnapshot();
            return ValueTask.CompletedTask;
        }
        private void RequireActive()
        {
            if (!_active || Disposals > 0) throw new InvalidOperationException("Нет действующей транзакции.");
        }
        private void RestoreSnapshot()
        {
            // Имитация восстановления хранилища: Domain не разрешает обратные переходы.
            foreach (var (version, state) in _snapshot)
                typeof(ObjectVersion).GetProperty(nameof(ObjectVersion.VersionType))!.SetValue(version, state);
            _active = false;
        }
    }
}
