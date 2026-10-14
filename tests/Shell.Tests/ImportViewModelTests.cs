using Application.Interfaces.Services;
using Application.Records.Objects;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class ImportViewModelTests
{
    [Fact]
    public async Task CancelledFolderSelectionDoesNotImportOrReadObjects()
    {
        var scenario = new Scenario();
        scenario.Picker.Folder = null;
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Equal(0, scenario.Import.Calls);
        Assert.Equal(0, scenario.Objects.SearchCalls);
        Assert.Null(scenario.ViewModel.ImportReport);
        Assert.Null(scenario.ViewModel.ImportFolder);
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.Null(scenario.ViewModel.ErrorMessage);
        Assert.Empty(scenario.Presenter.Reports);
        Assert.False(scenario.ViewModel.ShowImportReportCommand.CanExecute(null));
    }

    [Fact]
    public async Task PartialImportDisplaysActionsRejectionsWarningsAndCounts()
    {
        var scenario = new Scenario();
        scenario.Import.Report = new(
            [new("part.m3d", "Создан объект и версия 1.")],
            [new("broken.m3d", "Не удалось прочитать документ.")],
            [new("part.m3d", "У детали отсутствует масса.")]);

        await scenario.ViewModel.ImportCommand.ExecuteAsync();

        Assert.Equal(Scenario.Folder, scenario.Import.LastFolder);
        Assert.Equal(Scenario.Folder, scenario.ViewModel.ImportFolder);
        Assert.Same(scenario.Import.Report, scenario.ViewModel.ImportReport);
        var shown = Assert.Single(scenario.Presenter.Reports);
        Assert.Equal("Принято: 1. Отклонено: 1. Предупреждения: 1.", shown.Summary);
        Assert.Equal(Scenario.Folder, shown.FolderPath);
        Assert.Equal(scenario.Import.Report.Accepted, shown.Report.Accepted);
        Assert.Equal(scenario.Import.Report.Rejected, shown.Report.Rejected);
        Assert.Equal(scenario.Import.Report.Warnings, shown.Report.Warnings);
        Assert.Equal(1, scenario.Objects.SearchCalls);
        Assert.Single(scenario.ViewModel.Results);
        Assert.Null(scenario.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task ImportLoadsNewObjectsOutsideFilterSoTheyAppearWhenQueryChanges()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        var card = scenario.Objects.Card;
        var part = new PdmObjectSummary(card.Id, card.ObjectType, card.Designation, card.Name,
            card.CurrentVersion?.Id, 1, ObjectVersionType.InWork);
        var standard = part with { Id = Guid.NewGuid(), Name = "Болт М8", Designation = null, ObjectType = ObjectType.StandardPart };
        scenario.Import.Report = AcceptedReport();
        scenario.Objects.Search = query =>
        {
            Assert.Empty(query);
            return Task.FromResult<IReadOnlyList<PdmObjectSummary>>([part, standard]);
        };

        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Equal(part.Id, Assert.Single(scenario.ViewModel.Results).Id);
        scenario.ViewModel.SearchText = "болт";
        Assert.Same(standard, Assert.Single(scenario.ViewModel.Results));
        scenario.ViewModel.SearchText = string.Empty;
        Assert.Equal(2, scenario.ViewModel.Results.Count);
        Assert.Equal(2, scenario.Objects.SearchCalls);
    }

    [Fact]
    public async Task ReportOpensOnlyAfterImportAndObjectRefreshComplete()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        scenario.Import.Report = AcceptedReport();
        var pending = new TaskCompletionSource<PdmObjectDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Objects.Get = _ => pending.Task;
        var operation = scenario.ViewModel.ImportCommand.ExecuteAsync();
        try
        {
            Assert.Same(scenario.Import.Report, scenario.ViewModel.ImportReport);
            Assert.Empty(scenario.Presenter.Reports);
            Assert.True(scenario.ViewModel.IsBusy);
            Assert.False(scenario.ViewModel.ShowImportReportCommand.CanExecute(null));
        }
        finally
        {
            pending.TrySetResult(scenario.Objects.Card);
            await operation;
        }
        Assert.Single(scenario.Presenter.Reports);
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.True(scenario.ViewModel.ShowImportReportCommand.CanExecute(null));
    }

    [Fact]
    public async Task LastReportCanBeReopenedWithoutImportingOrReadingObjectsAgain()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        var original = Assert.Single(scenario.Presenter.Reports);

        await scenario.ViewModel.ShowImportReportCommand.ExecuteAsync();

        Assert.Equal(2, scenario.Presenter.Reports.Count);
        Assert.Same(original, scenario.Presenter.Reports[1]);
        Assert.Equal(1, scenario.Import.Calls);
        Assert.Equal(0, scenario.Objects.SearchCalls);
        Assert.Equal(0, scenario.Objects.GetCalls);
    }

    [Fact]
    public async Task WindowFailurePreservesCompletedImportAndAllowsOpeningReportAgain()
    {
        var scenario = new Scenario();
        scenario.Import.Report = AcceptedReport();
        scenario.Presenter.Present = _ => throw new Exception("Окно недоступно.");

        await scenario.ViewModel.ImportCommand.ExecuteAsync();

        Assert.Same(scenario.Import.Report, scenario.ViewModel.ImportReport);
        Assert.Contains("Окно недоступно.", scenario.ViewModel.ErrorMessage);
        Assert.StartsWith("Импорт завершён.", scenario.ViewModel.StatusMessage);
        Assert.Single(scenario.ViewModel.Results);
        Assert.True(scenario.ViewModel.ShowImportReportCommand.CanExecute(null));
        scenario.Presenter.Present = null;
        await scenario.ViewModel.ShowImportReportCommand.ExecuteAsync();
        var report = Assert.Single(scenario.Presenter.Reports);
        Assert.Equal(scenario.Import.Report.Accepted, report.Report.Accepted);
        Assert.Equal(1, scenario.Import.Calls);
        Assert.Null(scenario.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task ReportsFromDifferentImportsKeepTheirFolderAndResults()
    {
        var scenario = new Scenario();
        var accepted = new List<ImportAcceptedFile> { new("first.m3d", "Создан объект и версия 1.") };
        scenario.Import.Report = new(accepted, [], []);
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        var first = Assert.Single(scenario.Presenter.Reports);

        scenario.Picker.Folder = "second-synthetic-folder";
        accepted.Clear();
        accepted.Add(new("second.m3d", "Обновлена рабочая версия."));
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        var second = scenario.Presenter.Reports[1];

        Assert.NotSame(first, second);
        Assert.Equal(Scenario.Folder, first.FolderPath);
        Assert.Equal("first.m3d", Assert.Single(first.Report.Accepted).FileName);
        Assert.Equal("second-synthetic-folder", second.FolderPath);
        Assert.Equal("second.m3d", Assert.Single(second.Report.Accepted).FileName);
    }

    [Fact]
    public async Task SuccessfulImportRefreshesSearchAndCardPreservingSelectedHistoricalVersion()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        var previous = scenario.Objects.Card.CurrentVersion! with { VersionType = ObjectVersionType.Approved };
        var current = previous with { Id = Guid.NewGuid(), VersionNumber = 2, VersionType = ObjectVersionType.InWork, MassKg = 3m };
        var updated = scenario.Objects.Card with { Name = "Деталь новая", CurrentVersion = current, Versions = [current, previous] };
        scenario.Import.Execute = _ =>
        {
            scenario.Objects.Card = updated;
            return Task.FromResult(AcceptedReport());
        };

        await scenario.ViewModel.ImportCommand.ExecuteAsync();

        Assert.Equal(2, scenario.Objects.SearchCalls);
        Assert.Equal(2, scenario.Objects.GetCalls);
        Assert.Equal(string.Empty, scenario.Objects.LastQuery);
        Assert.Same(updated, scenario.ViewModel.Card);
        Assert.Equal(previous.Id, scenario.ViewModel.SelectedVersion!.Id);
        Assert.Equal(current.Id, scenario.ViewModel.Results[0].CurrentVersionId);
        Assert.Same(scenario.ViewModel.Results[0], scenario.ViewModel.SelectedObject);
        Assert.Equal("Текущая версия: 2", scenario.ViewModel.CurrentVersionDescription);
    }

    [Fact]
    public async Task ObjectNoLongerMatchingSearchIsRemovedAndCardCleared()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        scenario.Objects.Search = _ => Task.FromResult<IReadOnlyList<PdmObjectSummary>>([]);
        scenario.Import.Report = AcceptedReport();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Empty(scenario.ViewModel.Results);
        Assert.Null(scenario.ViewModel.SelectedObject);
        Assert.Null(scenario.ViewModel.Card);
        Assert.Null(scenario.ViewModel.SelectedVersion);
        Assert.Equal(1, scenario.Objects.GetCalls);
        Assert.NotNull(scenario.ViewModel.ImportReport);
    }

    [Fact]
    public async Task SelectedButUnopenedObjectRemainsSelectedWithoutLoadingCard()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.SearchCommand.ExecuteAsync();
        scenario.ViewModel.SelectedObject = Assert.Single(scenario.ViewModel.Results);
        scenario.Import.Report = AcceptedReport();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Same(scenario.ViewModel.Results[0], scenario.ViewModel.SelectedObject);
        Assert.Null(scenario.ViewModel.Card);
        Assert.Equal(0, scenario.Objects.GetCalls);
    }

    [Fact]
    public async Task AllRejectedFilesDoNotRefreshUnchangedObjects()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        var card = scenario.ViewModel.Card;
        scenario.Import.Report = new([], [new("broken.m3d", "Документ повреждён.")], []);
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Same(scenario.Import.Report, scenario.ViewModel.ImportReport);
        Assert.Same(card, scenario.ViewModel.Card);
        Assert.Equal(1, scenario.Objects.SearchCalls);
        Assert.Equal(1, scenario.Objects.GetCalls);
        Assert.Null(scenario.ViewModel.ErrorMessage);
        Assert.Single(scenario.Presenter.Reports);
    }

    [Fact]
    public async Task EmptyReportExplainsThatFolderContainsNoCadDocuments()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.NotNull(scenario.ViewModel.ImportReport);
        Assert.Contains("нет CAD-документов", scenario.ViewModel.StatusMessage);
        Assert.Equal(0, scenario.Objects.SearchCalls);
        Assert.Single(scenario.Presenter.Reports);
    }

    [Fact]
    public async Task FailedImportClearsPreviousReportAndKeepsObjectsWithoutClaimingSuccess()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        var card = scenario.ViewModel.Card;
        var searchCalls = scenario.Objects.SearchCalls;
        scenario.Import.Execute = _ => Task.FromException<ImportCadFolderReport>(new Exception("Ошибка сохранения."));

        await scenario.ViewModel.ImportCommand.ExecuteAsync();

        Assert.Null(scenario.ViewModel.ImportReport);
        Assert.Equal("Не удалось выполнить импорт папки.\nОшибка сохранения.", scenario.ViewModel.ErrorMessage);
        Assert.Equal("Операция не выполнена.", scenario.ViewModel.StatusMessage);
        Assert.Same(card, scenario.ViewModel.Card);
        Assert.Equal(searchCalls, scenario.Objects.SearchCalls);
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.True(scenario.ViewModel.ImportCommand.CanExecute(null));
        Assert.Single(scenario.Presenter.Reports);
        Assert.False(scenario.ViewModel.ShowImportReportCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshFailureKeepsSuccessfulReportAndClearsOutdatedObjects(bool failCard)
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        scenario.Import.Report = AcceptedReport();
        if (failCard)
            scenario.Objects.Get = _ => Task.FromException<PdmObjectDetails>(new Exception("Ошибка загрузки карточки."));
        else
            scenario.Objects.Search = _ => Task.FromException<IReadOnlyList<PdmObjectSummary>>(new Exception("Ошибка поиска."));

        await scenario.ViewModel.ImportCommand.ExecuteAsync();

        Assert.Same(scenario.Import.Report, scenario.ViewModel.ImportReport);
        Assert.StartsWith("Импорт завершён, но не удалось обновить", scenario.ViewModel.ErrorMessage);
        Assert.Equal("Импорт завершён. Требуется обновить данные.", scenario.ViewModel.StatusMessage);
        Assert.Empty(scenario.ViewModel.Results);
        Assert.Null(scenario.ViewModel.Card);
        Assert.Null(scenario.ViewModel.SelectedVersion);
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.True(scenario.ViewModel.SearchCommand.CanExecute(null));
        var shown = Assert.Single(scenario.Presenter.Reports);
        Assert.Equal(scenario.ViewModel.ErrorMessage, shown.Notification);
        await scenario.ViewModel.ShowImportReportCommand.ExecuteAsync();
        Assert.Same(shown, scenario.Presenter.Reports[1]);
        Assert.NotNull(scenario.Presenter.Reports[1].Notification);
    }

    [Fact]
    public async Task PendingImportBlocksAllOtherOperationsAndRepeatedFolderSelection()
    {
        var scenario = new Scenario();
        await scenario.OpenAsync();
        var pending = new TaskCompletionSource<ImportCadFolderReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Import.Execute = _ => pending.Task;
        var operation = scenario.ViewModel.ImportCommand.ExecuteAsync();
        try
        {
            Assert.True(scenario.ViewModel.IsBusy);
            Assert.False(scenario.ViewModel.SearchCommand.CanExecute(null));
            Assert.False(scenario.ViewModel.OpenObjectCommand.CanExecute(null));
            Assert.False(scenario.ViewModel.ApproveCommand.CanExecute(null));
            Assert.False(scenario.ViewModel.AnnulCommand.CanExecute(null));
            Assert.False(scenario.ViewModel.ImportCommand.CanExecute(null));
            await scenario.ViewModel.ImportCommand.ExecuteAsync();
            await scenario.ViewModel.SearchCommand.ExecuteAsync();
            await scenario.ViewModel.OpenObjectCommand.ExecuteAsync();
            Assert.Equal(1, scenario.Picker.Calls);
            Assert.Equal(1, scenario.Import.Calls);
            Assert.Equal(1, scenario.Objects.SearchCalls);
            Assert.Equal(1, scenario.Objects.GetCalls);
        }
        finally
        {
            pending.TrySetResult(new([], [], []));
            await operation;
        }
        Assert.False(scenario.ViewModel.IsBusy);
        Assert.True(scenario.ViewModel.ImportCommand.CanExecute(null));
    }

    [Fact]
    public async Task CancellingNextFolderSelectionPreservesPreviousReportAndFolder()
    {
        var scenario = new Scenario();
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        var previousReport = scenario.ViewModel.ImportReport;
        scenario.Picker.Folder = null;
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Same(previousReport, scenario.ViewModel.ImportReport);
        Assert.Equal(Scenario.Folder, scenario.ViewModel.ImportFolder);
        Assert.Equal(1, scenario.Import.Calls);
        Assert.Null(scenario.ViewModel.ErrorMessage);
    }

    [Fact]
    public async Task FolderDialogFailureIsHandledWithoutImport()
    {
        var scenario = new Scenario();
        scenario.Picker.Select = () => throw new Exception("Диалог недоступен.");
        await scenario.ViewModel.ImportCommand.ExecuteAsync();
        Assert.Contains("Диалог недоступен.", scenario.ViewModel.ErrorMessage);
        Assert.Equal(0, scenario.Import.Calls);
        Assert.False(scenario.ViewModel.IsBusy);
    }

    private static ImportCadFolderReport AcceptedReport() => new([new("part.m3d", "Обновлена рабочая версия.")], [], []);

    private sealed class Scenario
    {
        // Это только строка для подмен: папка и документы не создаются и не читаются.
        public const string Folder = "synthetic-import-folder";
        public FakeObjects Objects { get; } = new();
        public FakeImportService Import { get; } = new();
        public FakeFolderPicker Picker { get; } = new() { Folder = Folder };
        public FakeImportReportPresenter Presenter { get; } = new();
        public MainViewModel ViewModel { get; }

        public Scenario() => ViewModel = new(Objects, Import, Picker, new FakeCompositionReader(), Presenter) { SearchText = "  Деталь  " };

        public async Task OpenAsync()
        {
            await ViewModel.SearchCommand.ExecuteAsync();
            ViewModel.SelectedObject = Assert.Single(ViewModel.Results);
            await ViewModel.OpenObjectCommand.ExecuteAsync();
        }
    }

    private sealed class FakeObjects : IPdmObjectService
    {
        public PdmObjectDetails Card { get; set; }
        public Func<string, Task<IReadOnlyList<PdmObjectSummary>>>? Search { get; set; }
        public Func<Guid, Task<PdmObjectDetails>>? Get { get; set; }
        public int SearchCalls { get; private set; }
        public int GetCalls { get; private set; }
        public string? LastQuery { get; private set; }

        public FakeObjects()
        {
            var version = new PdmVersionDetails(Guid.NewGuid(), 1, ObjectVersionType.InWork, "Деталь", "Сталь", 2m, DateTime.UtcNow);
            Card = new(Guid.NewGuid(), ObjectType.Part, "АБВГ.123456.001", version.Name, version, [version]);
        }

        public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default)
        {
            SearchCalls++;
            LastQuery = searchText;
            return Search?.Invoke(searchText) ?? Task.FromResult<IReadOnlyList<PdmObjectSummary>>([
                new(Card.Id, Card.ObjectType, Card.Designation, Card.Name, Card.CurrentVersion?.Id,
                    Card.CurrentVersion?.VersionNumber, Card.CurrentVersion?.VersionType)]);
        }

        public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default)
        {
            GetCalls++;
            return Get?.Invoke(objectId) ?? Task.FromResult(Card);
        }

        public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
