using System.Collections.ObjectModel;
using Application.Records.Objects;
using Domain.Objects;
using Shell.Commands;

namespace Shell.ViewModels;

public sealed class ObjectTreeNodeViewModel : ObservableObject
{
    private PdmObjectSummary _object;
    private bool _isExpanded;
    private bool _isSelected;
    private readonly Action<ObjectTreeNodeViewModel>? _select;

    public PdmObjectSummary Object => _object;
    public IReadOnlyList<Guid> Path { get; }
    public int? Quantity { get; }
    public bool IsCycle { get; }
    public bool IsPlaceholder { get; }
    public bool IsLoaded { get; private set; }
    public ObservableCollection<ObjectTreeNodeViewModel> Children { get; } = [];
    public AsyncCommand LoadCommand { get; }
    public string QuantityText => Quantity is { } quantity ? $"×{quantity}" : string.Empty;
    public string VersionDescription => Object.CurrentVersionId is null ? "Нет текущей версии"
        : Object.CurrentVersionNumber is { } number ? $"Версия {number}" : "Текущая версия";
    public string? Diagnostic => IsPlaceholder ? null
        : IsCycle ? "Обнаружен цикл — дальнейшее раскрытие остановлено."
        : Object.CurrentVersionId is null ? "У объекта нет текущей версии." : null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value && !IsLoaded) LoadCommand.Execute(null);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value && !IsPlaceholder) _select?.Invoke(this);
        }
    }

    public ObjectTreeNodeViewModel(PdmObjectSummary obj, IReadOnlyList<Guid> path,
        Action<ObjectTreeNodeViewModel>? select, int? quantity = null, bool isCycle = false,
        Func<ObjectTreeNodeViewModel, Task>? load = null, Func<bool>? canLoad = null,
        bool isPlaceholder = false)
    {
        _object = obj;
        Path = Array.AsReadOnly(path.ToArray());
        Quantity = quantity;
        IsCycle = isCycle;
        IsPlaceholder = isPlaceholder;
        _select = select;
        IsLoaded = load is null || isCycle || obj.CurrentVersionId is null;
        LoadCommand = new AsyncCommand(() => load?.Invoke(this) ?? Task.CompletedTask,
            () => !IsLoaded && (canLoad?.Invoke() ?? true));
        if (!IsLoaded && obj.ObjectType == ObjectType.Assembly) Children.Add(Placeholder());
    }

    public void ApplyComposition(ObjectTreeNodeViewModel loaded)
    {
        _object = loaded.Object;
        OnPropertyChanged(nameof(Object));
        OnPropertyChanged(nameof(VersionDescription));
        OnPropertyChanged(nameof(Diagnostic));
        Children.Clear();
        foreach (var child in loaded.Children) Children.Add(child);
        IsLoaded = true;
        LoadCommand.NotifyCanExecuteChanged();
    }

    private static ObjectTreeNodeViewModel Placeholder() => new(
        new(Guid.Empty, ObjectType.Assembly, null, "Загрузка состава…", null, null, null),
        [], null, isPlaceholder: true);
}
