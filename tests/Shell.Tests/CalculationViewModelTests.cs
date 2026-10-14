using Application.Interfaces.Services;
using Application.Records.Calculations;
using Application.Records.Objects;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class CalculationViewModelTests
{
    [Fact]
    public async Task CalculationUsesAssemblyIdAndShowsMassAndSpecification()
    {
        var s = await Scenario.Open();
        var result = s.Result(5m);
        s.Calculator.Calculate = _ => Task.FromResult(result);
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Equal(s.Objects.Card.Id, Assert.Single(s.Calculator.Calls));
        Assert.Same(result, s.Vm.Calculation);
        Assert.Contains("5", s.Vm.CalculationDescription);
        Assert.Single(s.Vm.Calculation!.Specification);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Theory]
    [InlineData(ObjectType.Part)]
    [InlineData(ObjectType.StandardPart)]
    public async Task LeavesCannotBeCalculated(ObjectType type)
    {
        var s = await Scenario.Open(type);
        Assert.False(s.Vm.IsAssemblyCard);
        Assert.False(s.Vm.CalculateCommand.CanExecute(null));
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Empty(s.Calculator.Calls);
    }

    [Fact]
    public async Task AssemblyWithoutCurrentVersionCannotBeCalculated()
    {
        var s = await Scenario.Open();
        s.Objects.Card = s.Objects.Card with { CurrentVersion = null };
        await s.Vm.SearchCommand.ExecuteAsync();
        Assert.True(s.Vm.IsAssemblyCard);
        Assert.False(s.Vm.CalculateCommand.CanExecute(null));
    }

    [Fact]
    public async Task HistorySelectionDoesNotSwitchCalculationToHistoricalComposition()
    {
        var s = await Scenario.Open();
        var historical = s.Objects.Card.CurrentVersion! with { Id = Guid.NewGuid(), VersionNumber = 1, VersionType = ObjectVersionType.Approved };
        var current = s.Objects.Card.CurrentVersion! with { VersionNumber = 2 };
        s.Objects.Card = s.Objects.Card with { CurrentVersion = current, Versions = [current, historical] };
        await s.Vm.SearchCommand.ExecuteAsync();
        s.Vm.SelectedVersion = historical;
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Equal(s.Objects.Card.Id, Assert.Single(s.Calculator.Calls));
        Assert.NotNull(s.Vm.Calculation);
        Assert.Same(historical, s.Vm.SelectedVersion);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("approve")]
    [InlineData("annul")]
    [InlineData("import")]
    public async Task DataChangesInvalidatePreviousCalculation(string action)
    {
        var s = await Scenario.Open();
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.NotNull(s.Vm.Calculation);
        switch (action)
        {
            case "refresh": await s.Vm.SearchCommand.ExecuteAsync(); break;
            case "approve": await s.Vm.ApproveCommand.ExecuteAsync(); break;
            case "annul": await s.Vm.AnnulCommand.ExecuteAsync(); break;
            case "import": await s.Vm.ImportCommand.ExecuteAsync(); break;
        }
        Assert.Null(s.Vm.Calculation);
    }

    [Fact]
    public async Task SelectionChangeClearsCalculation()
    {
        var s = await Scenario.Open();
        await s.Vm.CalculateCommand.ExecuteAsync();
        s.Vm.SelectedObject = null;
        Assert.Null(s.Vm.Calculation);
        Assert.False(s.Vm.CalculateCommand.CanExecute(null));
    }

    [Fact]
    public async Task MissingMassIsDisplayedAsIssueWithoutPartialTotal()
    {
        var s = await Scenario.Open();
        var result = s.Result(null) with { Issues = [new(CalculationIssueKind.MissingMass, Guid.NewGuid(), "Деталь", "Масса не указана.", [])] };
        s.Calculator.Calculate = _ => Task.FromResult(result);
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Same(result, s.Vm.Calculation);
        Assert.Contains("нельзя", s.Vm.CalculationDescription);
        Assert.Contains("замечаниями", s.Vm.StatusMessage);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task FailedRecalculationClearsPreviousResultAndAllowsRetry()
    {
        var s = await Scenario.Open();
        await s.Vm.CalculateCommand.ExecuteAsync();
        s.Calculator.Calculate = _ => Task.FromException<AssemblyCalculationResult>(new InvalidOperationException("Нет соединения."));
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Null(s.Vm.Calculation);
        Assert.Contains("Нет соединения", s.Vm.ErrorMessage);
        Assert.True(s.Vm.CalculateCommand.CanExecute(null));
        s.Calculator.Calculate = null;
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.NotNull(s.Vm.Calculation);
        Assert.Null(s.Vm.ErrorMessage);
    }

    [Fact]
    public async Task PendingCalculationBlocksCommandsAndDoesNotShowResultForChangedSelection()
    {
        var s = await Scenario.Open();
        var completion = new TaskCompletionSource<AssemblyCalculationResult>();
        s.Calculator.Calculate = _ => completion.Task;
        var pending = s.Vm.CalculateCommand.ExecuteAsync();
        Assert.True(s.Vm.IsBusy);
        Assert.False(s.Vm.CalculateCommand.CanExecute(null));
        Assert.False(s.Vm.ImportCommand.CanExecute(null));
        Assert.False(s.Vm.ApproveCommand.CanExecute(null));
        s.Vm.SelectedObject = null;
        completion.SetResult(s.Result(5m));
        await pending;
        Assert.Null(s.Vm.Calculation);
        Assert.True(s.Vm.IsIdle);
    }

    [Fact]
    public async Task ResultForWrongAssemblyIsReportedAsError()
    {
        var s = await Scenario.Open();
        s.Calculator.Calculate = _ => Task.FromResult(s.Result(5m) with { AssemblyId = Guid.NewGuid() });
        await s.Vm.CalculateCommand.ExecuteAsync();
        Assert.Null(s.Vm.Calculation);
        Assert.Contains("другой сборки", s.Vm.ErrorMessage);
    }

    private sealed class Scenario
    {
        public ObjectsService Objects { get; } = new();
        public FakeCalculationService Calculator { get; } = new();
        public MainViewModel Vm { get; }
        private Scenario() => Vm = new(Objects, new FakeImportService
        {
            Report = new ImportCadFolderReport([new("root.a3d", "Обновлена рабочая версия.")], [], [])
        }, new FakeFolderPicker { Folder = "synthetic-folder" }, new FakeCompositionReader(), new FakeImportReportPresenter(), Calculator);
        public static async Task<Scenario> Open(ObjectType type = ObjectType.Assembly)
        {
            var s = new Scenario();
            s.Objects.Card = s.Objects.Card with { ObjectType = type };
            await s.Vm.InitializeAsync();
            s.Vm.SelectedObject = Assert.Single(s.Vm.Results);
            await s.Vm.OpenObjectCommand.ExecuteAsync();
            return s;
        }
        public AssemblyCalculationResult Result(decimal? mass) => new(Objects.Card.Id, mass,
            [new(Guid.NewGuid(), ObjectType.Part, "АБВГ.123456.002", "Деталь", "Сталь", 2m, mass / 2, mass)], []);
    }

    private sealed class ObjectsService : IPdmObjectService
    {
        public PdmObjectDetails Card { get; set; }
        public ObjectsService()
        {
            var v = new PdmVersionDetails(Guid.NewGuid(), 1, ObjectVersionType.InWork, "Сборка", null, null, DateTime.UtcNow);
            Card = new(Guid.NewGuid(), ObjectType.Assembly, "АБВГ.123456.001", "Сборка", v, [v]);
        }
        public Task<IReadOnlyList<PdmObjectSummary>> SearchAsync(string searchText, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PdmObjectSummary>>([new(Card.Id, Card.ObjectType, Card.Designation, Card.Name,
                Card.CurrentVersion?.Id, Card.CurrentVersion?.VersionNumber, Card.CurrentVersion?.VersionType)]);
        public Task<PdmObjectDetails> GetAsync(Guid objectId, CancellationToken ct = default) => Task.FromResult(Card);
        public Task<PdmObjectDetails> ApproveAsync(Guid objectId, Guid versionId, CancellationToken ct = default)
        {
            var current = Card.CurrentVersion! with { VersionType = ObjectVersionType.Approved };
            Card = Card with { CurrentVersion = current, Versions = [current] };
            return Task.FromResult(Card);
        }
        public Task<PdmObjectDetails> AnnulAsync(Guid objectId, Guid versionId, CancellationToken ct = default)
        {
            Card = Card with { CurrentVersion = null, Versions = [Card.CurrentVersion! with { VersionType = ObjectVersionType.Annulled }] };
            return Task.FromResult(Card);
        }
    }
}
