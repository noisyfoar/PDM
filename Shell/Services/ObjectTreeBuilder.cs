using Application.Records.Composition;
using Application.Records.Objects;
using Shell.ViewModels;

namespace Shell.Services;

public static class ObjectTreeBuilder
{
    public static ObjectTreeNodeViewModel Build(Guid rootId, IReadOnlyList<CompositionEntry> entries,
        Action<ObjectTreeNodeViewModel> select)
    {
        var nodes = new Dictionary<string, ObjectTreeNodeViewModel>(StringComparer.Ordinal);
        foreach (var entry in entries.OrderBy(entry => entry.Path.Count))
        {
            if (entry.Path.Count == 0 || entry.Path[0] != rootId || entry.Path[^1] != entry.ObjectId
                || entry.Depth != entry.Path.Count - 1
                || entry.ParentObjectId != (entry.Path.Count == 1 ? (Guid?)null : entry.Path[^2]))
                throw new InvalidOperationException("Сервис состава вернул некорректный путь вхождения.");

            var obj = new PdmObjectSummary(entry.ObjectId, entry.ObjectType, entry.Designation, entry.Name,
                entry.CurrentVersionId, entry.CurrentVersionNumber, entry.VersionType);
            var node = new ObjectTreeNodeViewModel(obj, entry.Path, select,
                entry.Depth == 0 ? null : entry.Quantity, entry.IsCycle);
            if (!nodes.TryAdd(Key(entry.Path), node))
                throw new InvalidOperationException("Сервис состава вернул повторяющийся путь вхождения.");
            if (entry.Depth == 0) continue;
            if (!nodes.TryGetValue(Key(entry.Path.Take(entry.Path.Count - 1)), out var parent))
                throw new InvalidOperationException("В составе отсутствует родитель вхождения.");
            if (parent.IsCycle || parent.Object.CurrentVersionId is null)
                throw new InvalidOperationException("Сервис состава вернул компоненты узла без доступного состава.");
            parent.Children.Add(node);
        }
        return nodes.GetValueOrDefault(Key([rootId]))
            ?? throw new InvalidOperationException("Корневой объект не найден в составе.");
    }

    private static string Key(IEnumerable<Guid> path) => string.Join("/", path);
}
