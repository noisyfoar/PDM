using Application.Interfaces.Services;
using Application.Records.Composition;
using Application.Records.Objects;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Shell.Services;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class ObjectTreeTests
{
    [Fact]
    public void BuilderKeepsRepeatedObjectsAsSeparateOccurrencesAndHandlesUnorderedRows()
    {
        var scenario = new Scenario();
        var tree = ObjectTreeBuilder.Build(scenario.RootId, scenario.Entries().Reverse().ToArray(), _ => { });
        var first = Assert.Single(tree.Children.Single(node => node.Object.Id == scenario.LeftId).Children);
        var second = Assert.Single(tree.Children.Single(node => node.Object.Id == scenario.RightId).Children);
        Assert.Equal(first.Object.Id, second.Object.Id);
        Assert.NotSame(first, second);
        Assert.Equal(new[] { scenario.RootId, scenario.LeftId, scenario.PartId }, first.Path);
        Assert.Equal(new[] { scenario.RootId, scenario.RightId, scenario.PartId }, second.Path);
        Assert.Equal(3, first.Quantity);
        Assert.Equal(7, second.Quantity);
        Assert.Equal("Версия 1", first.VersionDescription);
    }

    [Fact]
    public void CycleClosingNodeIsVisibleAndHasNoChildren()
    {
        var scenario = new Scenario();
        var root = scenario.Entries()[0];
        var closing = root with
        {
            ParentObjectId = scenario.RootId, Path = [scenario.RootId, scenario.RootId],
            Depth = 1, IsCycle = true, Quantity = 2
        };
        var tree = ObjectTreeBuilder.Build(scenario.RootId, [root, closing], _ => { });
        var child = Assert.Single(tree.Children);
        Assert.True(child.IsCycle);
        Assert.Empty(child.Children);
        Assert.Contains("цикл", child.Diagnostic);
        Assert.False(child.LoadCommand.CanExecute(null));
    }

    [Fact]
    public void MissingCurrentVersionRemainsVisibleWithDiagnostic()
    {
        var scenario = new Scenario();
        var entries = scenario.Entries();
        var missing = entries.Last() with { CurrentVersionId = null, CurrentVersionNumber = null, VersionType = null };
        var tree = ObjectTreeBuilder.Build(scenario.RootId, entries.Take(entries.Count - 1).Append(missing).ToArray(), _ => { });
        var child = Assert.Single(tree.Children.Single(node => node.Object.Id == scenario.RightId).Children);
        Assert.Equal("Нет текущей версии", child.VersionDescription);
        Assert.NotNull(child.Diagnostic);
        Assert.Empty(child.Children);
    }

    [Fact]
    public void DuplicateOccurrencePathIsRejected()
    {
        var scenario = new Scenario();
        var entries = scenario.Entries();
        Assert.Throws<InvalidOperationException>(() => ObjectTreeBuilder.Build(scenario.RootId,
            entries.Append(entries.Last()).ToArray(), _ => { }));
    }

    [Fact]
    public void OrphanOccurrenceIsRejected()
    {
        var scenario = new Scenario();
        var entries = scenario.Entries();
        Assert.Throws<InvalidOperationException>(() => ObjectTreeBuilder.Build(scenario.RootId,
            [entries[0], entries.Last()], _ => { }));
    }

    [Fact]
    public async Task InitializationCreatesAllRootsWithoutLoadingComposition()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.InitializeAsync();
        Assert.Equal(4, scenario.ViewModel.TreeRoots.Count);
        Assert.Empty(scenario.Reader.Roots);
        var root = scenario.Root;
        Assert.False(root.IsLoaded);
        Assert.True(Assert.Single(root.Children).IsPlaceholder);
        Assert.Empty(scenario.ViewModel.TreeRoots.Single(node => node.Object.Id == scenario.PartId).Children);
    }

    [Fact]
    public async Task FirstExpansionLoadsWholeSubtreeOnceAndNestedExpansionNeedsNoQuery()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.InitializeAsync();
        await scenario.ExpandAsync();
        var nested = scenario.Root.Children[0];
        nested.IsExpanded = true;
        await nested.LoadCommand.ExecuteAsync();
        await scenario.Root.LoadCommand.ExecuteAsync();
        Assert.Equal(scenario.RootId, Assert.Single(scenario.Reader.Roots));
        Assert.True(scenario.Root.IsLoaded);
        Assert.Equal(2, scenario.Root.Children.Count);
        Assert.Equal(3, Assert.Single(nested.Children).Quantity);
    }

    [Fact]
    public async Task ComponentSelectionOpensItsCardAndSurvivesFilterWhileParentMatches()
    {
        var scenario = new Scenario();
        await scenario.OpenComponentAsync();
        scenario.ViewModel.SearchText = "  главная  ";
        Assert.Equal(scenario.RootId, Assert.Single(scenario.ViewModel.TreeRoots).Object.Id);
        Assert.Equal(scenario.PartId, scenario.ViewModel.SelectedObject!.Id);
        Assert.Equal(scenario.PartId, scenario.ViewModel.Card!.Id);
        Assert.Equal(new[] { scenario.RootId, scenario.LeftId, scenario.PartId }, scenario.ViewModel.SelectedTreeNode!.Path);
        Assert.True(scenario.ViewModel.SelectedTreeNode.IsSelected);
        Assert.Equal(scenario.PartId, scenario.Objects.LastOpenedId);
        Assert.Single(scenario.Reader.Roots);

        scenario.ViewModel.SearchText = "Нет совпадений";
        Assert.Empty(scenario.ViewModel.TreeRoots);
        Assert.Null(scenario.ViewModel.Card);
        Assert.Null(scenario.ViewModel.SelectedTreeNode);
        scenario.ViewModel.SearchText = string.Empty;
        Assert.Equal(4, scenario.ViewModel.TreeRoots.Count);
        Assert.True(scenario.Root.IsLoaded);
        Assert.Single(scenario.Reader.Roots);
    }

    [Fact]
    public async Task FailedCompositionLoadKeepsRetryAvailable()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.InitializeAsync();
        scenario.Reader.Read = _ => Task.FromException<IReadOnlyList<CompositionEntry>>(new Exception("Ошибка чтения состава."));
        await scenario.Root.LoadCommand.ExecuteAsync();
        Assert.False(scenario.Root.IsLoaded);
        Assert.False(scenario.Root.IsExpanded);
        Assert.Contains("Ошибка чтения состава.", scenario.ViewModel.ErrorMessage);
        Assert.True(scenario.Root.LoadCommand.CanExecute(null));
        scenario.Reader.Read = _ => Task.FromResult(scenario.Entries());
        await scenario.Root.LoadCommand.ExecuteAsync();
        Assert.True(scenario.Root.IsLoaded);
        Assert.Null(scenario.ViewModel.ErrorMessage);
        Assert.Equal(2, scenario.Reader.Roots.Count);
    }

    [Fact]
    public async Task PendingCompositionLoadBlocksDuplicateAndIncompatibleOperations()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.InitializeAsync();
        var pending = new TaskCompletionSource<IReadOnlyList<CompositionEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Reader.Read = _ => pending.Task;
        var load = scenario.Root.LoadCommand.ExecuteAsync();
        try
        {
            Assert.True(scenario.ViewModel.IsBusy);
            Assert.False(scenario.ViewModel.ImportCommand.CanExecute(null));
            Assert.False(scenario.ViewModel.SearchCommand.CanExecute(null));
            await scenario.Root.LoadCommand.ExecuteAsync();
            Assert.Single(scenario.Reader.Roots);
        }
        finally
        {
            pending.TrySetResult(scenario.Entries());
            await load;
        }
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.True(scenario.Root.IsLoaded);
    }

    [Fact]
    public async Task StateChangeRefreshesSelectedOccurrenceAndAllRepeatedOccurrencesInExpandedBranch()
    {
        var scenario = new Scenario();
        await scenario.OpenComponentAsync();
        scenario.ViewModel.SearchText = "Главная";
        scenario.Objects.Annul = id =>
        {
            var original = scenario.Objects.Cards[id];
            var annulled = original.Versions[0] with { VersionType = ObjectVersionType.Annulled };
            return scenario.Objects.Cards[id] = original with { CurrentVersion = null, Versions = [annulled] };
        };
        await scenario.ViewModel.AnnulCommand.ExecuteAsync();
        Assert.Equal(2, scenario.Reader.Roots.Count);
        Assert.True(scenario.Root.IsExpanded);
        Assert.Equal(scenario.PartId, scenario.ViewModel.Card!.Id);
        Assert.Null(scenario.ViewModel.Card.CurrentVersion);
        Assert.Equal(new[] { scenario.RootId, scenario.LeftId, scenario.PartId }, scenario.ViewModel.SelectedTreeNode!.Path);
        Assert.All(scenario.Root.Children.SelectMany(node => node.Children), node =>
        {
            Assert.Null(node.Object.CurrentVersionId);
            Assert.NotNull(node.Diagnostic);
        });
        Assert.False(scenario.ViewModel.AnnulCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportRefreshesExpandedCompositionAndKeepsSelectedOccurrence()
    {
        var scenario = new Scenario();
        await scenario.OpenComponentAsync();
        scenario.ViewModel.SearchText = "Главная";
        var entries = scenario.Entries().Select(entry => entry.Depth == 2 ? entry with { Quantity = 9 } : entry).ToArray();
        scenario.Import.Execute = _ =>
        {
            scenario.Reader.Read = _ => Task.FromResult<IReadOnlyList<CompositionEntry>>(entries);
            return Task.FromResult(new ImportCadFolderReport([new("synthetic.a3d", "Обновлена рабочая версия.")], [], []));
        };
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Equal(2, scenario.Reader.Roots.Count);
        Assert.Equal(9, scenario.ViewModel.SelectedTreeNode!.Quantity);
        Assert.Equal(scenario.PartId, scenario.ViewModel.Card!.Id);
        Assert.True(scenario.Root.IsExpanded);
        Assert.True(scenario.Root.Children.Single(node => node.Object.Id == scenario.LeftId).IsExpanded);
        Assert.NotNull(scenario.ViewModel.ImportReport);
    }

    [Fact]
    public async Task AssemblyWithoutCurrentVersionHasNoLoadPlaceholderOrQuery()
    {
        var scenario = new Scenario();
        scenario.Objects.Cards[scenario.RootId] = scenario.Objects.Cards[scenario.RootId] with { CurrentVersion = null };
        await scenario.ViewModel.InitializeAsync();
        Assert.Empty(scenario.Root.Children);
        Assert.NotNull(scenario.Root.Diagnostic);
        await scenario.Root.LoadCommand.ExecuteAsync();
        Assert.Empty(scenario.Reader.Roots);
    }

    private sealed class Scenario
    {
        public Guid RootId { get; } = Guid.NewGuid();
        public Guid LeftId { get; } = Guid.NewGuid();
        public Guid RightId { get; } = Guid.NewGuid();
        public Guid PartId { get; } = Guid.NewGuid();
        public ObjectsService Objects { get; } = new();
        public FakeCompositionReader Reader { get; } = new();
        public FakeImportService Import { get; } = new();
        public MainViewModel ViewModel { get; }
        public ObjectTreeNodeViewModel Root => ViewModel.TreeRoots.Single(node => node.Object.Id == RootId);

        public Scenario()
        {
            Add(RootId, ObjectType.Assembly, "Главная сборка", 1);
            Add(LeftId, ObjectType.Assembly, "Подсборка 1", 2);
            Add(RightId, ObjectType.Assembly, "Подсборка 2", 3);
            Add(PartId, ObjectType.Part, "Деталь", 4);
            Reader.Read = _ => Task.FromResult(Entries());
            ViewModel = new(Objects, Import, new FakeFolderPicker { Folder = "synthetic-folder" }, Reader, new FakeImportReportPresenter(), new FakeCalculationService());
        }

        public IReadOnlyList<CompositionEntry> Entries() => [
            Entry(RootId, [RootId], 1), Entry(LeftId, [RootId, LeftId], 2),
            Entry(RightId, [RootId, RightId], 5), Entry(PartId, [RootId, LeftId, PartId], 3),
            Entry(PartId, [RootId, RightId, PartId], 7)];

        public async Task ExpandAsync()
        {
            await Root.LoadCommand.ExecuteAsync();
            Root.IsExpanded = true;
        }

        public async Task OpenComponentAsync()
        {
            await ViewModel.InitializeAsync();
            await ExpandAsync();
            var parent = Root.Children.Single(node => node.Object.Id == LeftId);
            parent.IsExpanded = true;
            Assert.Single(parent.Children).IsSelected = true;
            await ViewModel.OpenObjectCommand.ExecuteAsync();
        }

        private void Add(Guid id, ObjectType type, string name, int number)
        {
            var version = new PdmVersionDetails(Guid.NewGuid(), 1, ObjectVersionType.InWork, name,
                type == ObjectType.Part ? "Сталь" : null, type == ObjectType.Part ? 2m : null, DateTime.UtcNow);
            Objects.Cards.Add(id, new(id, type, $"АБВГ.123456.{number:000}", name, version, [version]));
        }

        private CompositionEntry Entry(Guid id, Guid[] path, int quantity)
        {
            var card = Objects.Cards[id];
            var current = card.CurrentVersion;
            return new(id, path.Length == 1 ? null : path[^2], current?.Id, card.ObjectType,
                card.Designation, card.Name, current?.VersionType, current?.Material, current?.MassKg,
                quantity, quantity, path.Length - 1, path, false, current?.VersionNumber);
        }
    }

    private sealed class ObjectsService : IPdmObjectService
    {
        public Dictionary<Guid, PdmObjectDetails> Cards { get; } = [];
        public Func<Guid, PdmObjectDetails>? Annul { get; set; }
        public Guid? LastOpenedId { get; private set; }
        public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PdmObjectSummary>>(Cards.Values.Select(card => new PdmObjectSummary(
                card.Id, card.ObjectType, card.Designation, card.Name, card.CurrentVersion?.Id,
                card.CurrentVersion?.VersionNumber, card.CurrentVersion?.VersionType)).ToArray());

        public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default)
        {
            LastOpenedId = objectId;
            return Task.FromResult(Cards[objectId]);
        }

        public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default) =>
            Task.FromResult(Annul!(objectId));
    }
}
