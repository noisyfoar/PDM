using Application.Interfaces.Services;
using Application.Records.Objects;
using Domain.Objects;
using Domain.Versions;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void InitiallySearchAndImportAreAvailable()
    {
        var vm = new MainViewModel(new FakeObjectService(), new FakeImportService(), new FakeFolderPicker(), new FakeCompositionReader(), new FakeImportReportPresenter(), new FakeCalculationService());
        Assert.True(vm.SearchCommand.CanExecute(null));
        Assert.True(vm.ImportCommand.CanExecute(null));
        Assert.False(vm.OpenObjectCommand.CanExecute(null));
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.False(vm.AnnulCommand.CanExecute(null));
        Assert.False(vm.HasCard);
    }

    [Fact]
    public async Task ReloadLoadsAllObjectsAndAppliesTrimmedFilter()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var another = Summary(CreateCard());
        service.Search = query =>
        {
            Assert.Empty(query);
            return Task.FromResult<IReadOnlyList<PdmObjectSummary>>([another]);
        };
        vm.SearchText = "  Деталь  ";

        await vm.SearchCommand.ExecuteAsync();

        Assert.Equal(another, Assert.Single(vm.Results));
        Assert.Null(vm.SelectedObject);
        Assert.Null(vm.Card);
        Assert.Null(vm.SelectedVersion);
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ClearingQueryRestoresAllObjectsWithoutAnotherRequest()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var calls = service.SearchCalls;
        vm.SearchText = "Нет совпадений";
        Assert.Empty(vm.Results);
        vm.SearchText = "   ";
        Assert.Equal(service.Card.Id, Assert.Single(vm.Results).Id);
        Assert.Null(vm.Card);
        Assert.Equal("Всего объектов: 1.", vm.StatusMessage);
        Assert.Equal(calls, service.SearchCalls);
    }

    [Fact]
    public async Task InitializationLoadsAllObjectsWithoutRequiringSearchInput()
    {
        var service = new FakeObjectService();
        var objects = SearchObjects(service.Card);
        service.Search = query =>
        {
            Assert.Empty(query);
            return Task.FromResult<IReadOnlyList<PdmObjectSummary>>(objects);
        };
        var vm = new MainViewModel(service, new FakeImportService(), new FakeFolderPicker(), new FakeCompositionReader(), new FakeImportReportPresenter(), new FakeCalculationService());
        await vm.InitializeAsync();
        Assert.Equal(objects, vm.Results.ToArray());
        Assert.Equal("Всего объектов: 3.", vm.StatusMessage);
        Assert.Equal(1, service.SearchCalls);
    }

    [Theory]
    [InlineData("  деТАль  ", 0)]
    [InlineData("болт", 1)]
    [InlineData("абвг.123456.002", 2)]
    [InlineData("Нет совпадений", -1)]
    public async Task TypingFiltersByNameOrDesignationWithoutAnotherRequest(string query, int index)
    {
        var service = new FakeObjectService();
        var objects = SearchObjects(service.Card);
        service.Search = _ => Task.FromResult<IReadOnlyList<PdmObjectSummary>>(objects);
        var vm = new MainViewModel(service, new FakeImportService(), new FakeFolderPicker(), new FakeCompositionReader(), new FakeImportReportPresenter(), new FakeCalculationService());
        await vm.InitializeAsync();

        vm.SearchText = query;

        if (index < 0) Assert.Empty(vm.Results);
        else Assert.Same(objects[index], Assert.Single(vm.Results));
        Assert.Equal(1, service.SearchCalls);
        vm.SearchText = string.Empty;
        Assert.Equal(objects, vm.Results.ToArray());
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task FilteringKeepsCardWhenSelectedObjectStillMatches()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var card = vm.Card;
        var version = vm.SelectedVersion;
        vm.SearchText = "  абвг.123456.001  ";
        Assert.Same(card, vm.Card);
        Assert.Same(version, vm.SelectedVersion);
        Assert.Same(vm.Results[0], vm.SelectedObject);
        Assert.True(vm.ApproveCommand.CanExecute(null));
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task LoadedResultsUseLatestTextIfQueryChangesDuringRequest()
    {
        var service = new FakeObjectService();
        var objects = SearchObjects(service.Card);
        var pending = new TaskCompletionSource<IReadOnlyList<PdmObjectSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Search = _ => pending.Task;
        var vm = new MainViewModel(service, new FakeImportService(), new FakeFolderPicker(), new FakeCompositionReader(), new FakeImportReportPresenter(), new FakeCalculationService());
        var load = vm.InitializeAsync();
        vm.SearchText = "болт";
        pending.SetResult(objects);
        await load;
        Assert.Same(objects[1], Assert.Single(vm.Results));
        Assert.Equal("Найдено объектов: 1.", vm.StatusMessage);
        Assert.False(vm.IsBusy);
        Assert.Equal(1, service.SearchCalls);
    }

    [Fact]
    public async Task OpeningSelectsCurrentVersionRatherThanNewestAnnulledVersion()
    {
        var working = Version(1, ObjectVersionType.InWork);
        var annulled = Version(2, ObjectVersionType.Annulled);
        var card = CreateCard(working, annulled, working);
        var service = new FakeObjectService { Card = card };
        var vm = await OpenAsync(service);
        Assert.Equal(card.Id, service.LastOpenedId);
        Assert.Same(card, vm.Card);
        Assert.Same(working, vm.SelectedVersion);
        Assert.Equal("Текущая версия: 1", vm.CurrentVersionDescription);
    }

    [Fact]
    public async Task SelectingAnotherObjectClearsPreviousCardAndVersion()
    {
        var vm = await OpenAsync(new FakeObjectService());
        vm.SelectedObject = Summary(CreateCard());
        Assert.Null(vm.Card);
        Assert.Null(vm.SelectedVersion);
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.True(vm.OpenObjectCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(ObjectVersionType.InWork, true, true)]
    [InlineData(ObjectVersionType.Approved, false, true)]
    [InlineData(ObjectVersionType.Annulled, false, false)]
    public async Task StateCommandsFollowSelectedVersion(ObjectVersionType state, bool approve, bool annul)
    {
        var version = Version(1, state);
        var service = new FakeObjectService
        {
            Card = CreateCard(state == ObjectVersionType.Annulled ? null : version, version)
        };
        var vm = await OpenAsync(service);
        Assert.Equal(approve, vm.ApproveCommand.CanExecute(null));
        Assert.Equal(annul, vm.AnnulCommand.CanExecute(null));
    }

    [Fact]
    public async Task VersionOutsideCardCannotBeChanged()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        vm.SelectedVersion = Version(9, ObjectVersionType.InWork);
        await vm.ApproveCommand.ExecuteAsync();
        await vm.AnnulCommand.ExecuteAsync();
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.False(vm.AnnulCommand.CanExecute(null));
        Assert.Equal(0, service.ApproveCalls);
        Assert.Equal(0, service.AnnulCalls);
    }

    [Fact]
    public async Task ApproveRefreshesCardResultAndSelectedVersion()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var oldVersion = vm.SelectedVersion!;
        var approved = oldVersion with { VersionType = ObjectVersionType.Approved };
        var updated = service.Card with { CurrentVersion = approved, Versions = [approved] };
        service.Approve = (_, _) => Task.FromResult(updated);

        await vm.ApproveCommand.ExecuteAsync();

        Assert.Equal((updated.Id, oldVersion.Id), service.LastChangedIds);
        Assert.Same(updated, vm.Card);
        Assert.Same(approved, vm.SelectedVersion);
        Assert.Equal(ObjectVersionType.Approved, Assert.Single(vm.Results).CurrentVersionType);
        Assert.Same(vm.Results[0], vm.SelectedObject);
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.True(vm.AnnulCommand.CanExecute(null));
        vm.SearchText = "Нет совпадений";
        vm.SearchText = string.Empty;
        Assert.Equal(ObjectVersionType.Approved, Assert.Single(vm.Results).CurrentVersionType);
    }

    [Fact]
    public async Task AnnulUsesSelectedHistoricalVersionAndPreservesCurrentVersion()
    {
        var old = Version(1, ObjectVersionType.Approved);
        var current = Version(2, ObjectVersionType.InWork);
        var service = new FakeObjectService { Card = CreateCard(current, current, old) };
        var vm = await OpenAsync(service);
        vm.SelectedVersion = old;
        var annulled = old with { VersionType = ObjectVersionType.Annulled };
        var updated = service.Card with { Versions = [current, annulled] };
        service.Annul = (_, _) => Task.FromResult(updated);

        await vm.AnnulCommand.ExecuteAsync();

        Assert.Equal((updated.Id, old.Id), service.LastChangedIds);
        Assert.Same(current, vm.Card!.CurrentVersion);
        Assert.Same(annulled, vm.SelectedVersion);
        Assert.Equal(current.Id, vm.Results[0].CurrentVersionId);
        Assert.False(vm.AnnulCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnnulLastActiveVersionShowsAbsenceOfCurrentVersion()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var annulled = service.Card.CurrentVersion! with { VersionType = ObjectVersionType.Annulled };
        var updated = service.Card with { CurrentVersion = null, Versions = [annulled] };
        service.Annul = (_, _) => Task.FromResult(updated);

        await vm.AnnulCommand.ExecuteAsync();

        Assert.True(vm.HasCard);
        Assert.Null(vm.Card!.CurrentVersion);
        Assert.Null(vm.Results[0].CurrentVersionId);
        Assert.Equal("Текущей версии нет.", vm.CurrentVersionDescription);
        Assert.Same(annulled, vm.SelectedVersion);
        Assert.False(vm.ApproveCommand.CanExecute(null));
        Assert.False(vm.AnnulCommand.CanExecute(null));
    }

    [Fact]
    public async Task AnnulCurrentVersionRefreshesResultWithPreviousActiveVersion()
    {
        var previous = Version(1, ObjectVersionType.Approved);
        var current = Version(2, ObjectVersionType.InWork);
        var service = new FakeObjectService { Card = CreateCard(current, current, previous) };
        var vm = await OpenAsync(service);
        var annulled = current with { VersionType = ObjectVersionType.Annulled };
        service.Annul = (_, _) => Task.FromResult(service.Card with
        {
            CurrentVersion = previous,
            Versions = [annulled, previous]
        });
        await vm.AnnulCommand.ExecuteAsync();
        Assert.Equal(previous.Id, vm.Results[0].CurrentVersionId);
        Assert.Equal("Текущая версия: 1", vm.CurrentVersionDescription);
        Assert.Equal(annulled.Id, vm.SelectedVersion!.Id);
    }

    [Fact]
    public async Task PendingRequestBlocksAllCommandsAndRepeatedExecution()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var pending = new TaskCompletionSource<PdmObjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Get = _ => pending.Task;
        var originalGetCalls = service.GetCalls;
        var originalSearchCalls = service.SearchCalls;
        var request = vm.OpenObjectCommand.ExecuteAsync();
        try
        {
            Assert.True(vm.IsBusy);
            Assert.False(vm.IsIdle);
            Assert.False(vm.SearchCommand.CanExecute(null));
            Assert.False(vm.OpenObjectCommand.CanExecute(null));
            Assert.False(vm.ApproveCommand.CanExecute(null));
            Assert.False(vm.AnnulCommand.CanExecute(null));
            Assert.False(vm.ImportCommand.CanExecute(null));
            await vm.OpenObjectCommand.ExecuteAsync();
            await vm.SearchCommand.ExecuteAsync();
            await vm.ApproveCommand.ExecuteAsync();
            await vm.AnnulCommand.ExecuteAsync();
            await vm.ImportCommand.ExecuteAsync();
            Assert.Equal(originalGetCalls + 1, service.GetCalls);
            Assert.Equal(originalSearchCalls, service.SearchCalls);
            Assert.Equal(0, service.ApproveCalls);
            Assert.Equal(0, service.AnnulCalls);
        }
        finally
        {
            pending.TrySetResult(service.Card);
            await request;
        }
        Assert.False(vm.IsBusy);
        Assert.True(vm.SearchCommand.CanExecute(null));
        Assert.True(vm.ApproveCommand.CanExecute(null));
    }

    [Fact]
    public async Task SearchFailureKeepsExistingDataAndNextSearchClearsError()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var selected = vm.SelectedObject;
        service.Search = _ => Task.FromException<IReadOnlyList<PdmObjectSummary>>(new Exception("База недоступна."));
        await vm.SearchCommand.ExecuteAsync();
        Assert.Equal("База недоступна.", vm.ErrorMessage);
        Assert.Same(service.Card, vm.Card);
        Assert.Same(selected, vm.SelectedObject);
        Assert.Single(vm.Results);
        Assert.False(vm.IsBusy);

        service.Search = _ => Task.FromResult<IReadOnlyList<PdmObjectSummary>>([]);
        await vm.SearchCommand.ExecuteAsync();
        Assert.Null(vm.ErrorMessage);
        Assert.Null(vm.Card);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task FailedStateChangeKeepsOriginalCardAndState()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var selected = vm.SelectedVersion;
        service.Approve = (_, _) => Task.FromException<PdmObjectDetails>(new Exception("Версия уже аннулирована."));
        await vm.ApproveCommand.ExecuteAsync();
        Assert.Equal("Версия уже аннулирована.", vm.ErrorMessage);
        Assert.Same(service.Card, vm.Card);
        Assert.Same(selected, vm.SelectedVersion);
        Assert.Equal(ObjectVersionType.InWork, vm.Results[0].CurrentVersionType);
        Assert.True(vm.ApproveCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedOpenDoesNotShowPreviousObjectsCard()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        vm.SelectedObject = Summary(CreateCard());
        service.Get = _ => Task.FromException<PdmObjectDetails>(new Exception("Объект не найден."));
        await vm.OpenObjectCommand.ExecuteAsync();
        Assert.Null(vm.Card);
        Assert.Null(vm.SelectedVersion);
        Assert.Equal("Объект не найден.", vm.ErrorMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ResultReplacementCanResetGridSelectionWithoutLosingUpdatedCard()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        // При замене строки WPF может сбросить SelectedItem через двустороннюю привязку.
        vm.Results.CollectionChanged += (_, _) => vm.SelectedObject = null;
        var approved = service.Card.CurrentVersion! with { VersionType = ObjectVersionType.Approved };
        service.Approve = (_, _) => Task.FromResult(service.Card with
        {
            CurrentVersion = approved,
            Versions = [approved]
        });
        await vm.ApproveCommand.ExecuteAsync();
        Assert.NotNull(vm.Card);
        Assert.Same(approved, vm.SelectedVersion);
        Assert.Same(vm.Results[0], vm.SelectedObject);
    }

    [Fact]
    public async Task RefreshRereadsOpenCardAndPreservesSelectedHistoricalVersion()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        var original = vm.SelectedVersion!;
        var current = Version(2, ObjectVersionType.InWork);
        var updated = service.Card with { CurrentVersion = current, Versions = [current, original] };
        service.Search = _ => Task.FromResult<IReadOnlyList<PdmObjectSummary>>([Summary(updated)]);
        service.Get = _ => Task.FromResult(updated);

        await vm.SearchCommand.ExecuteAsync();

        Assert.Same(updated, vm.Card);
        Assert.Equal(2, vm.Card!.CurrentVersion!.VersionNumber);
        Assert.Equal(2, vm.Card.Versions.Count);
        Assert.Same(original, vm.SelectedVersion);
        Assert.Equal(2, vm.SelectedObject!.CurrentVersionNumber);
        Assert.Equal(2, service.GetCalls);
    }

    [Fact]
    public async Task RefreshFailureClearsStaleCardAndAllowsRetry()
    {
        var service = new FakeObjectService();
        var vm = await OpenAsync(service);
        service.Get = _ => Task.FromException<PdmObjectDetails>(new InvalidOperationException("Ошибка чтения карточки."));
        await vm.SearchCommand.ExecuteAsync();
        Assert.Null(vm.Card);
        Assert.Null(vm.SelectedVersion);
        Assert.Contains("Ошибка чтения карточки", vm.ErrorMessage);
        Assert.True(vm.IsIdle);
        service.Get = null;
        await vm.OpenObjectCommand.ExecuteAsync();
        Assert.NotNull(vm.Card);
        Assert.Null(vm.ErrorMessage);
    }

    private static async Task<MainViewModel> OpenAsync(FakeObjectService service)
    {
        var vm = new MainViewModel(service, new FakeImportService(), new FakeFolderPicker(), new FakeCompositionReader(), new FakeImportReportPresenter(), new FakeCalculationService()) { SearchText = "Деталь" };
        await vm.SearchCommand.ExecuteAsync();
        vm.SelectedObject = Assert.Single(vm.Results);
        await vm.OpenObjectCommand.ExecuteAsync();
        return vm;
    }

    private static PdmVersionDetails Version(int number, ObjectVersionType state) =>
        new(Guid.NewGuid(), number, state, "Деталь", "Сталь", 2m, new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc));

    private static PdmObjectDetails CreateCard()
    {
        var current = Version(1, ObjectVersionType.InWork);
        return CreateCard(current, current);
    }

    private static PdmObjectDetails CreateCard(PdmVersionDetails? current, params PdmVersionDetails[] versions) =>
        new(Guid.NewGuid(), ObjectType.Part, "АБВГ.123456.001", "Деталь", current, versions);

    private static PdmObjectSummary Summary(PdmObjectDetails card) =>
        new(card.Id, card.ObjectType, card.Designation, card.Name, card.CurrentVersion?.Id,
            card.CurrentVersion?.VersionNumber, card.CurrentVersion?.VersionType);

    private static PdmObjectSummary[] SearchObjects(PdmObjectDetails card)
    {
        var part = Summary(card);
        return [part,
            part with { Id = Guid.NewGuid(), Name = "Болт М8", Designation = null, ObjectType = ObjectType.StandardPart },
            part with { Id = Guid.NewGuid(), Name = "Корпус", Designation = "АБВГ.123456.002" }];
    }

    private sealed class FakeObjectService : IPdmObjectService
    {
        public PdmObjectDetails Card { get; init; } = CreateCard();
        public Func<string, Task<IReadOnlyList<PdmObjectSummary>>>? Search { get; set; }
        public Func<Guid, Task<PdmObjectDetails>>? Get { get; set; }
        public Func<Guid, Guid, Task<PdmObjectDetails>>? Approve { get; set; }
        public Func<Guid, Guid, Task<PdmObjectDetails>>? Annul { get; set; }
        public int SearchCalls { get; private set; }
        public int GetCalls { get; private set; }
        public int ApproveCalls { get; private set; }
        public int AnnulCalls { get; private set; }
        public Guid? LastOpenedId { get; private set; }
        public (Guid, Guid)? LastChangedIds { get; private set; }

        public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default)
        {
            SearchCalls++;
            return Search?.Invoke(searchText) ?? Task.FromResult<IReadOnlyList<PdmObjectSummary>>([Summary(Card)]);
        }

        public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default)
        {
            GetCalls++;
            LastOpenedId = objectId;
            return Get?.Invoke(objectId) ?? Task.FromResult(Card);
        }

        public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default)
        {
            ApproveCalls++;
            LastChangedIds = (objectId, versionId);
            return Approve?.Invoke(objectId, versionId) ?? Task.FromResult(Card);
        }

        public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default)
        {
            AnnulCalls++;
            LastChangedIds = (objectId, versionId);
            return Annul?.Invoke(objectId, versionId) ?? Task.FromResult(Card);
        }
    }
}
