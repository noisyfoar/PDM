using System.Collections.ObjectModel;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Records.Objects;
using Contracts.CAD.Records;
using Domain.Objects;
using Domain.Versions;
using Shell.Commands;
using Shell.Interfaces;
using Shell.Services;

namespace Shell.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly IPdmObjectService _objectService;
    private readonly IImportCadFolderService _importService;
    private readonly IFolderPicker _folderPicker;
    private readonly ICompositionReader _compositionReader;
    private readonly IImportReportPresenter _reportPresenter;
    private readonly List<PdmObjectSummary> _objects = [];
    private readonly Dictionary<Guid, ObjectTreeNodeViewModel> _rootNodes = [];
    private ObjectTreeNodeViewModel? _selectedTreeNode;
    private string _searchText = string.Empty;
    private PdmObjectSummary? _selectedObject;
    private PdmObjectDetails? _card;
    private PdmVersionDetails? _selectedVersion;
    private bool _isBusy;
    private string? _errorMessage;
    private string _statusMessage = "Загрузите список объектов.";
    private ImportCadFolderReport? _importReport;
    private ImportReportViewModel? _lastReportViewModel;
    private string? _importFolder;

    public string Title => "PDM";
    public ObservableCollection<PdmObjectSummary> Results { get; } = [];
    public ObservableCollection<ObjectTreeNodeViewModel> TreeRoots { get; } = [];
    public AsyncCommand SearchCommand { get; }
    public AsyncCommand OpenObjectCommand { get; }
    public AsyncCommand ApproveCommand { get; }
    public AsyncCommand AnnulCommand { get; }
    public AsyncCommand ImportCommand { get; }
    public AsyncCommand ShowImportReportCommand { get; }

    public string? ImportFolder { get => _importFolder; private set => SetProperty(ref _importFolder, value); }
    public ImportCadFolderReport? ImportReport
    {
        get => _importReport;
        private set
        {
            if (SetProperty(ref _importReport, value)) ShowImportReportCommand.NotifyCanExecuteChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(updateStatus: IsIdle);
        }
    }

    public PdmObjectSummary? SelectedObject
    {
        get => _selectedObject;
        set
        {
            if (ReferenceEquals(_selectedObject, value)) return;
            _selectedObject = value;
            OnPropertyChanged();
            if (_selectedTreeNode is not null && _selectedTreeNode.Object.Id != value?.Id)
            {
                _selectedTreeNode.IsSelected = false;
                _selectedTreeNode = null;
                OnPropertyChanged(nameof(SelectedTreeNode));
            }
            // При обновлении строки результата карточка того же объекта остаётся открытой.
            if (Card?.Id != value?.Id)
            {
                Card = null;
                SelectedVersion = null;
            }
            OpenObjectCommand.NotifyCanExecuteChanged();
        }
    }

    public ObjectTreeNodeViewModel? SelectedTreeNode
    {
        get => _selectedTreeNode;
        set
        {
            if (ReferenceEquals(_selectedTreeNode, value) || value?.IsPlaceholder == true) return;
            if (_selectedTreeNode is not null) _selectedTreeNode.IsSelected = false;
            _selectedTreeNode = value;
            OnPropertyChanged();
            if (value is not null) value.IsSelected = true;
            SelectedObject = value?.Object;
        }
    }

    public PdmObjectDetails? Card
    {
        get => _card;
        private set
        {
            if (!SetProperty(ref _card, value)) return;
            OnPropertyChanged(nameof(HasCard));
            OnPropertyChanged(nameof(CurrentVersionDescription));
            NotifyCommands();
        }
    }

    public bool HasCard => Card is not null;
    public string CurrentVersionDescription => Card?.CurrentVersion is { } version
        ? $"Текущая версия: {version.VersionNumber}"
        : "Текущей версии нет.";

    public PdmVersionDetails? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (!SetProperty(ref _selectedVersion, value)) return;
            NotifyCommands();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            NotifyCommands();
        }
    }

    public bool IsIdle => !IsBusy;
    public string? ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public MainViewModel(IPdmObjectService objectService, IImportCadFolderService importService,
        IFolderPicker folderPicker, ICompositionReader compositionReader, IImportReportPresenter reportPresenter)
    {
        _objectService = objectService ?? throw new ArgumentNullException(nameof(objectService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _compositionReader = compositionReader ?? throw new ArgumentNullException(nameof(compositionReader));
        _reportPresenter = reportPresenter ?? throw new ArgumentNullException(nameof(reportPresenter));
        SearchCommand = new AsyncCommand(SearchAsync, () => IsIdle);
        OpenObjectCommand = new AsyncCommand(OpenObjectAsync, () => IsIdle && SelectedObject is not null);
        ApproveCommand = new AsyncCommand(() => ChangeStateAsync(approve: true),
            () => CanChangeState && SelectedVersion?.VersionType == ObjectVersionType.InWork);
        AnnulCommand = new AsyncCommand(() => ChangeStateAsync(approve: false),
            () => CanChangeState && SelectedVersion?.VersionType is ObjectVersionType.InWork or ObjectVersionType.Approved);
        ImportCommand = new AsyncCommand(ImportAsync, () => IsIdle);
        ShowImportReportCommand = new AsyncCommand(ShowImportReportAsync, () => IsIdle && _lastReportViewModel is not null);
    }

    // Это доступность кнопок. Правила переходов повторно проверяются Domain при сохранении.
    private bool CanChangeState => IsIdle && Card is not null && SelectedVersion is not null
        && Card.Versions.Any(version => version.Id == SelectedVersion.Id);

    public Task InitializeAsync() => SearchCommand.ExecuteAsync();

    private Task SearchAsync() => RunAsync(async () =>
    {
        var objects = await _objectService.SearchAsync(string.Empty);
        await ReplaceObjectsAsync(objects);
        ApplyFilter();
    }, "Загрузка объектов…");

    private void ApplyFilter(bool updateStatus = true)
    {
        var selectedId = SelectedObject?.Id;
        var selectedPath = SelectedTreeNode?.Path;
        var card = Card;
        var version = SelectedVersion;
        var query = SearchText.Trim();
        var matches = _objects.Where(obj => Matches(obj, query)).ToArray();
        Results.Clear();
        foreach (var obj in matches) Results.Add(obj);
        var roots = matches.Select(obj => _rootNodes[obj.Id]).ToArray();
        TreeRoots.Clear();
        foreach (var root in roots) TreeRoots.Add(root);
        var node = selectedPath is null ? null
            : roots.Where(root => root.Object.Id == selectedPath[0])
                .Select(root => FindNode(root, selectedPath)).FirstOrDefault();
        node ??= roots.FirstOrDefault(root => root.Object.Id == selectedId);
        SelectedTreeNode = node;
        if (node is not null) node.IsSelected = true;
        SelectedObject = node?.Object;
        // Перестройка списка может сбросить SelectedItem через привязку WPF.
        if (card is not null && SelectedObject?.Id == card.Id)
        {
            Card = card;
            SelectedVersion = version;
        }
        else
        {
            Card = null;
            SelectedVersion = null;
        }
        if (updateStatus)
            StatusMessage = query.Length == 0
                ? $"Всего объектов: {_objects.Count}." : $"Найдено объектов: {Results.Count}.";
    }

    private static bool Matches(PdmObjectSummary obj, string query) =>
        query.Length == 0 || obj.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || obj.Designation?.Contains(query, StringComparison.OrdinalIgnoreCase) == true;

    private Task OpenObjectAsync()
    {
        var objectId = SelectedObject!.Id;
        return RunAsync(async () =>
        {
            ApplyCard(await _objectService.GetAsync(objectId));
            StatusMessage = "Карточка загружена.";
        }, "Загрузка карточки…");
    }

    private Task ChangeStateAsync(bool approve)
    {
        var objectId = Card!.Id;
        var version = SelectedVersion!;
        return RunAsync(async () =>
        {
            var card = approve
                ? await _objectService.ApproveAsync(objectId, version.Id)
                : await _objectService.AnnulAsync(objectId, version.Id);
            await UpdateResultAsync(card);
            if (SelectedObject?.Id == card.Id) ApplyCard(card, version.Id);
            StatusMessage = approve
                ? $"Версия {version.VersionNumber} утверждена."
                : $"Версия {version.VersionNumber} аннулирована.";
        }, "Сохранение состояния версии…");
    }

    private Task ImportAsync() => RunAsync(async () =>
    {
        var folder = _folderPicker.SelectFolder();
        if (string.IsNullOrWhiteSpace(folder))
        {
            StatusMessage = "Выбор папки отменён.";
            return;
        }

        ImportFolder = folder;
        ImportReport = null;
        _lastReportViewModel = null;
        ImportReport = await _importService.ExecuteAsync(folder);
        StatusMessage = ImportReport.Accepted.Count + ImportReport.Rejected.Count == 0
            ? "В папке нет CAD-документов для импорта."
            : "Импорт завершён.";

        if (ImportReport.Accepted.Count > 0)
        {
            try { await RefreshAfterImportAsync(); }
            catch (Exception error)
            {
                // Импорт уже сохранён. Ошибка чтения не отменяет его результат.
                SelectedObject = null;
                Card = null;
                SelectedVersion = null;
                Results.Clear();
                _objects.Clear();
                _rootNodes.Clear();
                TreeRoots.Clear();
                ErrorMessage = $"Импорт завершён, но не удалось обновить список и карточку. Нажмите «Обновить».\n{error.Message}";
                StatusMessage = "Импорт завершён. Требуется обновить данные.";
            }
        }
        _lastReportViewModel = new ImportReportViewModel(ImportFolder!, ImportReport, ErrorMessage);
        TryShowImportReport();
    }, "Импорт папки…", "Не удалось выполнить импорт папки.");

    private Task ShowImportReportAsync() => RunAsync(() =>
    {
        if (TryShowImportReport()) StatusMessage = "Отчёт импорта открыт.";
        return Task.CompletedTask;
    }, "Открытие отчёта…");

    private bool TryShowImportReport()
    {
        try
        {
            _reportPresenter.Show(_lastReportViewModel!);
            return true;
        }
        catch (Exception error)
        {
            var message = $"Не удалось открыть окно отчёта.\n{error.Message}";
            ErrorMessage = ErrorMessage is null ? message : $"{ErrorMessage}\n{message}";
            StatusMessage = "Импорт завершён. Не удалось открыть отчёт.";
            return false;
        }
    }

    private async Task RefreshAfterImportAsync()
    {
        var selectedId = SelectedObject?.Id;
        var cardId = Card?.Id;
        var versionId = SelectedVersion?.Id;
        var objects = await _objectService.SearchAsync(string.Empty);
        await ReplaceObjectsAsync(objects);
        var card = SelectedObject?.Id == selectedId && selectedId == cardId && selectedId is { } id
            ? await _objectService.GetAsync(id) : null;
        if (card is not null) ApplyCard(card, versionId);
    }

    private void ApplyCard(PdmObjectDetails card, Guid? selectedVersionId = null)
    {
        Card = card;
        SelectedVersion = card.Versions.FirstOrDefault(version => version.Id == selectedVersionId)
            ?? card.CurrentVersion ?? card.Versions.FirstOrDefault();
    }

    private async Task UpdateResultAsync(PdmObjectDetails card)
    {
        var index = _objects.FindIndex(obj => obj.Id == card.Id);
        if (index < 0) return;
        var current = card.CurrentVersion;
        var summary = new PdmObjectSummary(card.Id, card.ObjectType, card.Designation, card.Name,
            current?.Id, current?.VersionNumber, current?.VersionType);
        var objects = _objects.ToArray();
        objects[index] = summary;
        await ReplaceObjectsAsync(objects);
    }

    private async Task ReplaceObjectsAsync(IReadOnlyList<PdmObjectSummary> objects)
    {
        var selectedRootId = SelectedTreeNode?.Path[0];
        var roots = objects.ToDictionary(obj => obj.Id, CreateRootNode);
        // Обновляем раскрытую ветвь выбора. Остальные ветви перечитаются при раскрытии.
        if (selectedRootId is { } rootId && _rootNodes.TryGetValue(rootId, out var previous)
            && previous.IsExpanded && roots.TryGetValue(rootId, out var root)
            && !root.IsLoaded && Matches(root.Object, SearchText.Trim()))
        {
            try
            {
                await PopulateRootAsync(root);
                root.IsExpanded = true;
            }
            catch (Exception error)
            {
                ErrorMessage = $"Не удалось обновить состав. Раскройте сборку повторно.\n{error.Message}";
            }
        }
        _objects.Clear();
        _objects.AddRange(objects);
        _rootNodes.Clear();
        foreach (var pair in roots) _rootNodes.Add(pair.Key, pair.Value);
        ApplyFilter(updateStatus: false);
    }

    private ObjectTreeNodeViewModel CreateRootNode(PdmObjectSummary obj) => new(obj, [obj.Id],
        node => SelectedTreeNode = node,
        load: obj.ObjectType == ObjectType.Assembly ? LoadRootAsync : null,
        canLoad: () => IsIdle);

    private Task LoadRootAsync(ObjectTreeNodeViewModel root) => RunAsync(async () =>
    {
        try
        {
            await PopulateRootAsync(root);
            if (ReferenceEquals(SelectedTreeNode, root)) SelectedObject = root.Object;
            StatusMessage = "Состав сборки загружен.";
        }
        catch
        {
            root.IsExpanded = false;
            throw;
        }
    }, "Загрузка состава…", "Не удалось загрузить состав сборки.");

    private async Task PopulateRootAsync(ObjectTreeNodeViewModel root)
    {
        var entries = await _compositionReader.GetAsync(root.Object.Id);
        root.ApplyComposition(ObjectTreeBuilder.Build(root.Object.Id, entries, node => SelectedTreeNode = node));
    }

    private static ObjectTreeNodeViewModel? FindNode(ObjectTreeNodeViewModel root, IReadOnlyList<Guid> path)
    {
        var node = root;
        for (var depth = 1; depth < path.Count; depth++)
        {
            if (!node.IsLoaded) return null;
            node.IsExpanded = true;
            node = node.Children.FirstOrDefault(child => !child.IsPlaceholder && child.Path[depth] == path[depth]);
            if (node is null) return null;
        }
        return node;
    }

    private async Task RunAsync(Func<Task> operation, string progressMessage, string? errorContext = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = progressMessage;
        try { await operation(); }
        catch (OperationCanceledException) { StatusMessage = "Операция отменена."; }
        catch (Exception error)
        {
            ErrorMessage = errorContext is null ? error.Message : $"{errorContext}\n{error.Message}";
            StatusMessage = "Операция не выполнена.";
        }
        finally { IsBusy = false; }
    }

    private void NotifyCommands()
    {
        SearchCommand.NotifyCanExecuteChanged();
        OpenObjectCommand.NotifyCanExecuteChanged();
        ApproveCommand.NotifyCanExecuteChanged();
        AnnulCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        ShowImportReportCommand.NotifyCanExecuteChanged();
    }
}
