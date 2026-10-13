using System.Reflection;
using Application.Interfaces.Persistence;
using Application.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Xunit;

namespace Infrastructure.Tests;

public sealed class ImportCadFolderServiceTests
{
    private static CadDocument Part(string file = "part.m3d", int number = 1, decimal? mass = 2m) =>
        new(1, file, CadDocumentType.Part, $"АБВГ.123456.{number:000}", "Деталь", "Сталь", mass, []);
    private static CadDocument Assembly(string file = "root.a3d", int number = 2, params CadComponent[] components) =>
        new(1, file, CadDocumentType.Assembly, $"АБВГ.123456.{number:000}", "Сборка", null, null, components);
    private static CadDocument Standard(string file = "bolt.m3d", string name = "Болт") =>
        new(1, file, CadDocumentType.StandardPart, null, name, null, 0.1m, []);
    private static Task<ImportCadFolderReport> Import(Work work, params CadDocument[] documents) =>
        Service(work, new CadFolderReadResult(documents, [])).ExecuteAsync("папка");
    private static ImportCadFolderService Service(Work work, CadFolderReadResult result) =>
        new(new Reader(result), new CadDomainMapper(), work);

    [Fact]
    public async Task NewAssemblyReferencesExistingChildAndIsIndependentOfDocumentOrder()
    {
        var part = new CadDomainMapper().Map(Part());
        var work = new Work(part);
        var report = await Import(work, Assembly(components: [new("PART.M3D", 3)]), Part());
        Assert.Equal(2, report.Accepted.Count);
        Assert.Empty(report.Rejected);
        Assert.Equal(2, work.Saved.Count);
        var root = Assert.Single(work.Saved.Values, item => item.ObjectType == ObjectType.Assembly);
        var component = Assert.Single(root.CurrentVersion!.Components);
        Assert.Same(part, component.ChildObject);
        Assert.Equal(3, component.Quantity);
        Assert.Single(part.Versions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task InvalidQuantityRejectsWholeAssemblyAndItsParents(int quantity)
    {
        var work = new Work();
        var report = await Import(work, Assembly("parent.a3d", 3, new CadComponent("root.a3d", 1)),
            Assembly(components: [new("part.m3d", quantity)]), Part());
        Assert.Single(report.Accepted);
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal(ObjectType.Part, Assert.Single(work.Saved.Values).ObjectType);
    }

    [Fact]
    public async Task DuplicateDesignationRejectsAllDuplicatesAndDependentAssembly()
    {
        var work = new Work();
        var report = await Import(work, Part(), Assembly("duplicate.a3d", 1),
            Assembly(components: [new("part.m3d", 1)]), Standard());
        Assert.Equal(3, report.Rejected.Count);
        Assert.Equal("bolt.m3d", Assert.Single(report.Accepted).FileName);
        Assert.Equal(ObjectType.StandardPart, Assert.Single(work.Saved.Values).ObjectType);
    }

    [Fact]
    public async Task DuplicateStandardNamesAreRejected()
    {
        var work = new Work();
        var report = await Import(work, Standard("first.m3d"), Standard("second.m3d"), Part());
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal("part.m3d", Assert.Single(report.Accepted).FileName);
    }

    [Fact]
    public async Task ReadErrorRejectsParentsButIndependentFilesAreImported()
    {
        var work = new Work();
        var report = await Service(work, new CadFolderReadResult(
            [Assembly(components: [new("broken.m3d", 1)]), Part()],
            [new CadReadError("папка/broken.m3d", "Некорректный документ.")])).ExecuteAsync("папка");
        Assert.Equal(2, report.Rejected.Count);
        Assert.Contains(report.Rejected, row => row.FileName == "root.a3d" && row.Reason.Contains("отклонён"));
        Assert.Single(report.Accepted);
    }

    [Fact]
    public async Task MissingFileIsNotReplacedWithObjectFromDatabase()
    {
        var work = new Work(new CadDomainMapper().Map(Part()));
        var report = await Import(work, Assembly(components: [new("part.m3d", 1)]));
        Assert.Contains("отсутствует", Assert.Single(report.Rejected).Reason);
        Assert.Empty(report.Accepted);
        Assert.Equal(0, work.Begins);
    }

    [Fact]
    public async Task InvalidAttributesRejectDocumentAndDependentAssemblies()
    {
        var work = new Work();
        var report = await Import(work, Part() with { Material = null },
            Assembly(components: [new("part.m3d", 1)]), Standard());
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal("bolt.m3d", Assert.Single(report.Accepted).FileName);
    }

    [Fact]
    public async Task PartWithoutMassIsAcceptedWithWarning()
    {
        var work = new Work();
        var report = await Import(work, Part(mass: null), Assembly(components: [new("part.m3d", 2)]));
        Assert.Equal(2, report.Accepted.Count);
        Assert.Empty(report.Rejected);
        Assert.Equal("part.m3d", Assert.Single(report.Warnings).FileName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DirectAndIndirectCyclesRejectParticipantsAndParents(bool selfReference)
    {
        var cycle = selfReference
            ? new[] { Assembly("a.a3d", 2, new CadComponent("a.a3d", 1)) }
            : new[] { Assembly("a.a3d", 2, new CadComponent("b.a3d", 1)), Assembly("b.a3d", 3, new CadComponent("a.a3d", 1)) };
        var documents = cycle.Concat([Assembly("parent.a3d", 4, new CadComponent("a.a3d", 1)), Part()]).ToArray();
        var work = new Work();
        var report = await Import(work, documents);
        Assert.Equal(cycle.Length + 1, report.Rejected.Count);
        Assert.Equal("part.m3d", Assert.Single(report.Accepted).FileName);
        Assert.All(cycle, doc => Assert.Contains(report.Rejected,
            row => row.FileName == doc.FileName && row.Reason.Contains("циклическая")));
    }

    [Fact]
    public async Task SharedComponentDoesNotCreateFalseCycle()
    {
        var work = new Work();
        var report = await Import(work, Assembly("root.a3d", 4, new("a.a3d", 2), new("b.a3d", 3)),
            Assembly("a.a3d", 2, new CadComponent("part.m3d", 1)), Assembly("b.a3d", 3, new CadComponent("part.m3d", 1)), Part());
        Assert.Empty(report.Rejected);
        Assert.Equal(4, report.Accepted.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedImportDoesNotCreateVersionForWorkingOrApprovedObject(bool approved)
    {
        var source = new CadDomainMapper().Map(Part());
        if (approved) source.CurrentVersion!.Approve();
        var versionId = source.CurrentVersion!.Id;
        var report = await Import(new Work(source), Part());
        Assert.Equal("Пропущен: изменений нет.", Assert.Single(report.Accepted).Action);
        Assert.Single(source.Versions);
        Assert.Equal(versionId, source.CurrentVersion.Id);
    }

    [Theory]
    [InlineData(ObjectVersionType.InWork)]
    [InlineData(ObjectVersionType.Approved)]
    [InlineData(ObjectVersionType.Annulled)]
    public async Task ChangedImportUsesCorrectVersionPolicy(ObjectVersionType state)
    {
        var source = new CadDomainMapper().Map(Part());
        var old = source.CurrentVersion!;
        if (state == ObjectVersionType.Approved) old.Approve();
        if (state == ObjectVersionType.Annulled) old.Annul();
        var report = await Import(new Work(source), Part() with { Name = "Обновлённая деталь" });
        Assert.Empty(report.Rejected);
        Assert.Equal(state == ObjectVersionType.InWork ? 1 : 2, source.Versions.Count);
        Assert.Equal("Обновлённая деталь", source.CurrentVersion!.Name);
        Assert.Equal(ObjectVersionType.InWork, source.CurrentVersion.VersionType);
        if (state != ObjectVersionType.InWork) Assert.Equal("Деталь", old.Name);
    }

    [Fact]
    public async Task ReorderedCompositionIsUnchangedButQuantityChangeUpdatesWorkingVersion()
    {
        var work = new Work();
        await Import(work, Part(), Standard(), Assembly(components: [new("part.m3d", 2), new("bolt.m3d", 3)]));
        var root = Assert.Single(work.Saved.Values, item => item.ObjectType == ObjectType.Assembly);
        var versionId = root.CurrentVersion!.Id;
        var same = await Import(work, Assembly(components: [new("bolt.m3d", 3), new("part.m3d", 2)]), Standard(), Part());
        Assert.Equal("Пропущен: изменений нет.", Assert.Single(same.Accepted, row => row.FileName == "root.a3d").Action);
        var changed = await Import(work, Assembly(components: [new("part.m3d", 5)]), Part());
        Assert.Equal("Обновлена рабочая версия.", Assert.Single(changed.Accepted, row => row.FileName == "root.a3d").Action);
        Assert.Equal(versionId, root.CurrentVersion.Id);
        Assert.Equal(5, Assert.Single(root.CurrentVersion.Components).Quantity);
    }

    [Fact]
    public async Task ApprovedCompositionRetainsHistoricalLinks()
    {
        var work = new Work();
        await Import(work, Assembly(components: [new("part.m3d", 2)]), Part());
        var root = Assert.Single(work.Saved.Values, item => item.ObjectType == ObjectType.Assembly);
        var old = root.CurrentVersion!;
        old.Approve();
        await Import(work, Assembly(components: [new("part.m3d", 5)]), Part());
        Assert.Equal(2, Assert.Single(old.Components).Quantity);
        Assert.Equal(5, Assert.Single(root.CurrentVersion!.Components).Quantity);
        Assert.Equal(2, root.Versions.Count);
    }

    [Fact]
    public async Task ExistingTypeConflictRejectsDependentAssembly()
    {
        var work = new Work(new CadDomainMapper().Map(Assembly("existing.a3d", 1)));
        var report = await Import(work, Part(), Assembly(components: [new("part.m3d", 1)]), Standard());
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal("bolt.m3d", Assert.Single(report.Accepted).FileName);
        Assert.Equal(2, work.Saved.Count);
    }

    [Fact]
    public async Task VersionLimitRejectsChangedObjectAndParentsWithoutBlockingIndependentFile()
    {
        var source = new CadDomainMapper().Map(Part());
        source.CurrentVersion!.Approve();
        typeof(ObjectVersion).GetField("<VersionNumber>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(source.CurrentVersion, int.MaxValue);
        var work = new Work(source);
        var report = await Import(work, Part() with { Name = "Изменена" },
            Assembly(components: [new("part.m3d", 1)]), Standard());
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal("bolt.m3d", Assert.Single(report.Accepted).FileName);
        Assert.Equal("Деталь", source.Name);
        Assert.Single(source.Versions);
        Assert.Equal(2, work.Saved.Count);
    }

    [Fact]
    public async Task AllRejectedAfterDatabaseLookupRollsBackWithoutCommit()
    {
        var work = new Work(new CadDomainMapper().Map(Assembly("existing.a3d", 1)));
        var report = await Import(work, Part());
        Assert.Single(report.Rejected);
        Assert.Empty(report.Accepted);
        Assert.Equal(1, work.Rollbacks);
        Assert.Equal(0, work.Commits);
    }

    [Fact]
    public async Task SavingFailureRollsBackAndPreservesOriginalException()
    {
        var original = new InvalidOperationException("Ошибка сохранения.");
        var work = new Work { CommitError = original };
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => Import(work, Part()));
        Assert.Same(original, actual);
        Assert.Equal(1, work.Rollbacks);
        Assert.False(work.RollbackToken.CanBeCanceled);
        Assert.Empty(work.Saved);
    }

    [Fact]
    public async Task RollbackFailurePreservesBothExceptions()
    {
        var original = new InvalidOperationException("Ошибка сохранения.");
        var rollback = new InvalidOperationException("Ошибка отката.");
        var work = new Work { CommitError = original, RollbackError = rollback };
        var actual = await Assert.ThrowsAsync<AggregateException>(() => Import(work, Part()));
        Assert.Equal(new[] { original, rollback }, actual.InnerExceptions);
    }

    [Fact]
    public async Task CancellationAfterReadingEmptyFolderIsNotIgnored()
    {
        using var cancellation = new CancellationTokenSource();
        var work = new Work();
        var service = new ImportCadFolderService(new Reader(new([], []), cancellation.Cancel), new CadDomainMapper(), work);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExecuteAsync("папка", cancellation.Token));
        Assert.Equal(0, work.Begins);
    }

    private sealed class Reader(CadFolderReadResult result, Action? onRead = null) : ICadFolderDocumentReader
    {
        public Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default)
        {
            onRead?.Invoke();
            return Task.FromResult(result);
        }
    }

    private sealed class Work(params PDMObject[] initial) : IUnitOfWork, IPdmObjectRepository
    {
        public Dictionary<Guid, PDMObject> Saved { get; } = initial.ToDictionary(item => item.Id);
        private readonly List<PDMObject> _pending = [];
        public IPdmObjectRepository Objects => this;
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }
        public CancellationToken RollbackToken { get; private set; }
        public Exception? CommitError { get; init; }
        public Exception? RollbackError { get; init; }
        public Task BeginAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); Begins++; return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Commits++;
            if (CommitError is not null) return Task.FromException(CommitError);
            foreach (var item in _pending) Saved.Add(item.Id, item);
            _pending.Clear();
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            Rollbacks++;
            RollbackToken = ct;
            _pending.Clear();
            return RollbackError is null ? Task.CompletedTask : Task.FromException(RollbackError);
        }
        public void Add(PDMObject obj) => _pending.Add(obj);
        public Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Saved.GetValueOrDefault(id));
        public Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default) =>
            Task.FromResult(Saved.Values.SingleOrDefault(obj => obj.Designation?.Value == designation.Value));
        public Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(Saved.Values.SingleOrDefault(obj => obj.ObjectType == ObjectType.StandardPart && obj.Name == name));
        public Task<IReadOnlyList<PDMObject>> SearchAsync(string text, CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
