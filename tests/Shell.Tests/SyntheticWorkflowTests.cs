using System.IO;
using System.Text.Json;
using Application.Interfaces.Persistence;
using Application.Records.Composition;
using Application.Services;
using Domain.Objects;
using Infrastructure.Cad;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class SyntheticWorkflowTests
{
    [Fact]
    public async Task FolderImportSearchTreeCalculationApprovalReimportAndAnnulmentWorkTogether()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"pdm_workflow_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            await WritePart(2m);
            await Write("bolt.m3d", new { formatVersion = 1, type = "StandardPart", name = "Болт", properties = new { mass = 0.5m } });
            await Write("sub.a3d", new { formatVersion = 1, type = "Assembly", designation = "АБВГ.123456.002", name = "Подсборка",
                components = new[] { new { file = "part.m3d", count = 4 }, new { file = "bolt.m3d", count = 2 } } });
            await Write("root.a3d", new { formatVersion = 1, type = "Assembly", designation = "АБВГ.123456.003", name = "Главная сборка",
                components = new[] { new { file = "sub.a3d", count = 3 }, new { file = "part.m3d", count = 2 } } });
            await File.WriteAllTextAsync(Path.Combine(folder, "broken.m3d"), "{");

            var storage = new Storage();
            var composition = new MemoryCompositionReader(storage);
            var reports = new FakeImportReportPresenter();
            var vm = new MainViewModel(new PdmObjectService(storage),
                new ImportCadFolderService(new CadFolderDocumentReader(new JsonCadDocumentReader()), new CadDomainMapper(), storage),
                new FakeFolderPicker { Folder = folder }, composition, reports, new AssemblyCalculationService(composition));

            await vm.InitializeAsync();
            Assert.Empty(vm.Results);
            await vm.ImportCommand.ExecuteAsync();
            Assert.Null(vm.ErrorMessage);
            var report = Assert.Single(reports.Reports).Report;
            Assert.Equal(4, report.Accepted.Count);
            Assert.Equal("broken.m3d", Assert.Single(report.Rejected).FileName);
            Assert.Equal(4, vm.Results.Count);
            vm.SearchText = "Главная";
            var root = Assert.Single(vm.TreeRoots);
            vm.SelectedTreeNode = root;
            await root.LoadCommand.ExecuteAsync();
            Assert.Equal(2, root.Children.Count);
            var sub = Assert.Single(root.Children, node => node.Object.ObjectType == ObjectType.Assembly);
            Assert.Equal(3, sub.Quantity);
            Assert.Equal(4, Assert.Single(sub.Children, node => node.Object.ObjectType == ObjectType.Part).Quantity);
            await vm.OpenObjectCommand.ExecuteAsync();
            await vm.CalculateCommand.ExecuteAsync();
            Assert.Equal(31m, vm.Calculation!.MassKg);
            Assert.Equal(14m, Assert.Single(vm.Calculation.Specification, row => row.ObjectType == ObjectType.Part).Quantity);

            vm.SearchText = "Деталь";
            vm.SelectedTreeNode = Assert.Single(vm.TreeRoots);
            await vm.OpenObjectCommand.ExecuteAsync();
            var partId = vm.Card!.Id;
            var firstVersion = vm.Card.CurrentVersion!;
            await vm.ApproveCommand.ExecuteAsync();
            Assert.Null(vm.ErrorMessage);
            await WritePart(3m);
            await vm.ImportCommand.ExecuteAsync();
            Assert.Null(vm.ErrorMessage);
            Assert.Equal(partId, vm.Card!.Id);
            Assert.Equal(2, vm.Card.CurrentVersion!.VersionNumber);
            Assert.Equal(3m, vm.Card.CurrentVersion.MassKg);
            Assert.Equal(2, vm.Card.Versions.Count);
            Assert.Equal(firstVersion.Id, vm.SelectedVersion!.Id);
            Assert.Equal("Создана новая рабочая версия.", reports.Reports[^1].Report.Accepted.Single(row => row.FileName == "part.m3d").Action);
            Assert.Equal("Пропущен: изменений нет.", reports.Reports[^1].Report.Accepted.Single(row => row.FileName == "root.a3d").Action);

            vm.SearchText = "Главная";
            vm.SelectedTreeNode = Assert.Single(vm.TreeRoots);
            await vm.OpenObjectCommand.ExecuteAsync();
            await vm.CalculateCommand.ExecuteAsync();
            Assert.Equal(45m, vm.Calculation!.MassKg);
            Assert.Equal(1, vm.Card!.CurrentVersion!.VersionNumber);

            vm.SearchText = "Деталь";
            vm.SelectedTreeNode = Assert.Single(vm.TreeRoots);
            await vm.OpenObjectCommand.ExecuteAsync();
            await vm.AnnulCommand.ExecuteAsync();
            Assert.Equal(firstVersion.Id, vm.Card!.CurrentVersion!.Id);
            vm.SearchText = "Главная";
            vm.SelectedTreeNode = Assert.Single(vm.TreeRoots);
            await vm.OpenObjectCommand.ExecuteAsync();
            await vm.CalculateCommand.ExecuteAsync();
            Assert.Equal(31m, vm.Calculation!.MassKg);
            Assert.Null(vm.ErrorMessage);
            Assert.Equal(4, storage.Saved.Count);
            Assert.All(storage.Works, work => Assert.True(work.Disposed));

            Task Write(string name, object document) => File.WriteAllTextAsync(Path.Combine(folder, name), JsonSerializer.Serialize(document));
            Task WritePart(decimal mass) => Write("part.m3d", new { formatVersion = 1, type = "Part", designation = "АБВГ.123456.001",
                name = "Деталь", properties = new { material = "Сталь", mass } });
        }
        finally
        {
            // Удаляем только созданные этим тестом файлы из его собственного временного каталога.
            foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    // Подмена хранения для успешного сценария; транзакции и CTE PostgreSQL здесь не имитируются.
    private sealed class Storage : IUnitOfWorkFactory
    {
        public Dictionary<Guid, PDMObject> Saved { get; } = [];
        public List<Work> Works { get; } = [];
        public IUnitOfWork CreateUnitOfWork()
        {
            var work = new Work(this);
            Works.Add(work);
            return work;
        }
    }

    private sealed class Work(Storage storage) : IUnitOfWork, IPdmObjectRepository
    {
        private bool _active;
        private readonly List<PDMObject> _pending = [];
        public bool Disposed { get; private set; }
        public IPdmObjectRepository Objects => this;
        public Task BeginAsync(CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); _active = true; return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct = default)
        {
            RequireActive(ct);
            foreach (var obj in _pending) storage.Saved.Add(obj.Id, obj);
            _pending.Clear();
            _active = false;
            return Task.CompletedTask;
        }
        public Task RollbackAsync(CancellationToken ct = default)
        {
            RequireActive(ct);
            _pending.Clear();
            _active = false;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() { _active = false; Disposed = true; return ValueTask.CompletedTask; }
        public void Add(PDMObject obj) { RequireActive(); _pending.Add(obj); }
        public Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            RequireActive(ct);
            return Task.FromResult(storage.Saved.GetValueOrDefault(id));
        }
        public Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default)
        {
            RequireActive(ct);
            return Task.FromResult(storage.Saved.Values.SingleOrDefault(obj => obj.Designation?.Value == designation.Value));
        }
        public Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default)
        {
            RequireActive(ct);
            return Task.FromResult(storage.Saved.Values.SingleOrDefault(obj => obj.ObjectType == ObjectType.StandardPart && obj.Name == name));
        }
        public Task<IReadOnlyList<PDMObject>> SearchAsync(string text, CancellationToken ct = default)
        {
            RequireActive(ct);
            return Task.FromResult<IReadOnlyList<PDMObject>>(storage.Saved.Values.Where(obj =>
                obj.Name.Contains(text, StringComparison.OrdinalIgnoreCase) || obj.Designation?.Value.Contains(text, StringComparison.OrdinalIgnoreCase) == true).ToArray());
        }
        private void RequireActive(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_active || Disposed) throw new InvalidOperationException("Нет активной единицы работы.");
        }
    }

    private sealed class MemoryCompositionReader(Storage storage) : ICompositionReader
    {
        public Task<IReadOnlyList<CompositionEntry>> GetAsync(Guid rootObjectId, CancellationToken ct = default)
        {
            var entries = new List<CompositionEntry>();
            Visit(storage.Saved[rootObjectId], [], 1, 1m);
            return Task.FromResult<IReadOnlyList<CompositionEntry>>(entries);

            void Visit(PDMObject obj, Guid[] parents, int quantity, decimal total)
            {
                ct.ThrowIfCancellationRequested();
                var version = obj.CurrentVersion;
                Guid[] path = [.. parents, obj.Id];
                var cycle = parents.Contains(obj.Id);
                entries.Add(new(obj.Id, parents.Length == 0 ? null : parents[^1], version?.Id, obj.ObjectType,
                    obj.Designation?.Value, obj.Name, version?.VersionType, version?.Material, version?.MassKg,
                    quantity, total, parents.Length, path, cycle, version?.VersionNumber));
                if (cycle || version is null) return;
                foreach (var component in version.Components)
                    Visit(component.ChildObject, path, component.Quantity, checked(total * component.Quantity));
            }
        }
    }
}
