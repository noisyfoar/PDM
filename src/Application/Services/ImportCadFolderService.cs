using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Domain.Components;
using Domain.Exceptions;
using Domain.Objects;
using Domain.Versions;

namespace Application.Services;

public sealed class ImportCadFolderService : IImportCadFolderService
{
    private static readonly StringComparer FileNames = StringComparer.OrdinalIgnoreCase;
    private readonly ICadFolderDocumentReader _reader;
    private readonly ICadDomainMapper _mapper;
    private readonly IUnitOfWorkFactory _factory;

    public ImportCadFolderService(ICadFolderDocumentReader reader, ICadDomainMapper mapper, IUnitOfWorkFactory factory)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public async Task<ImportCadFolderReport> ExecuteAsync(string folderPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        var readResult = await _reader.ReadAsync(folderPath, ct);
        ct.ThrowIfCancellationRequested();
        var accepted = new List<ImportAcceptedFile>();
        var warnings = new List<ImportWarning>();
        var rejected = new Dictionary<string, string>(FileNames);
        foreach (var error in readResult.Errors)
            rejected.TryAdd(Path.GetFileName(error.Path), error.Message);
        RejectDuplicates(readResult.Documents, rejected);

        var items = new Dictionary<string, ImportItem>(FileNames);
        foreach (var document in readResult.Documents.Where(document => !rejected.ContainsKey(document.FileName)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ValidateComponents(document);
                items.Add(document.FileName, new ImportItem(document, _mapper.Map(document)));
            }
            catch (Exception error) when (error is PDMException or ArgumentException)
            {
                rejected.TryAdd(document.FileName, error.Message);
            }
        }
        RejectInvalidDependencies(items, rejected, ct);
        if (!ValidItems().Any()) return Report();

        await using var work = _factory.CreateUnitOfWork();
        await work.BeginAsync(ct);
        try
        {
            foreach (var item in ValidItems())
            {
                var existing = await FindExistingAsync(work.Objects, item.Imported, ct);
                if (existing is not null && existing.ObjectType != item.Imported.ObjectType)
                    rejected.TryAdd(item.Document.FileName, "Тип документа не совпадает с типом объекта с этим обозначением.");
                else
                    item.Target = existing ?? item.Imported;
            }
            RejectInvalidDependencies(items, rejected, ct);

            // Проверяем все изменения до мутации сущностей: отказ одного файла не должен попасть в БД.
            foreach (var item in ValidItems())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    item.Components = item.Document.Components.Select(link =>
                        new Component(items[link.FileName].Target, link.Quantity)).ToArray();
                    var current = item.Target.CurrentVersion;
                    item.HasChanges = item.IsNew || current is null
                        || !SameContent(current, item.Imported.CurrentVersion!, item.Components);
                    if (!item.IsNew && item.HasChanges && current?.VersionType != ObjectVersionType.InWork)
                        item.Target.EnsureCanCreateNewVersion();
                }
                catch (Exception error) when (error is PDMException or ArgumentException)
                {
                    rejected.TryAdd(item.Document.FileName, error.Message);
                }
            }
            RejectInvalidDependencies(items, rejected, ct);
            if (!ValidItems().Any())
            {
                await work.RollbackAsync(CancellationToken.None);
                return Report();
            }

            foreach (var item in ValidItems())
            {
                ct.ThrowIfCancellationRequested();
                accepted.Add(new ImportAcceptedFile(item.Document.FileName, ApplyImport(item)));
                if (item.Document.Type == CadDocumentType.Part && item.Document.MassKg is null)
                    warnings.Add(new ImportWarning(item.Document.FileName, "У детали отсутствует масса."));
            }
            // Состав уже ссылается на единственные экземпляры существующих или новых объектов.
            foreach (var item in ValidItems().Where(item => item.IsNew))
                work.Objects.Add(item.Target);
            await work.CommitAsync(ct);
        }
        catch (Exception importError)
        {
            try { await work.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackError)
            {
                throw new AggregateException("Не удалось выполнить импорт и откатить транзакцию.", importError, rollbackError);
            }
            throw;
        }
        return Report();

        IEnumerable<ImportItem> ValidItems() => items.Values.Where(item => !rejected.ContainsKey(item.Document.FileName));
        ImportCadFolderReport Report() => new(accepted.AsReadOnly(),
            rejected.Select(item => new ImportRejectedFile(item.Key, item.Value)).ToList().AsReadOnly(), warnings.AsReadOnly());
    }

    private static Task<PDMObject?> FindExistingAsync(IPdmObjectRepository objects, PDMObject imported, CancellationToken ct) =>
        imported.ObjectType == ObjectType.StandardPart
            ? objects.GetStandardPartByNameAsync(imported.Name, ct)
            : objects.GetByDesignationAsync(imported.Designation!, ct);

    private static string ApplyImport(ImportItem item)
    {
        if (!item.HasChanges) return "Пропущен: изменений нет.";
        var current = item.Target.CurrentVersion;
        var createVersion = !item.IsNew && current?.VersionType != ObjectVersionType.InWork;
        var target = createVersion ? item.Target.CreateNewVersion() : current!;
        var source = item.Imported.CurrentVersion!;
        target.Update(source.Name, source.Material, source.MassKg, item.Components);
        return item.IsNew ? "Создан объект и версия 1."
            : createVersion ? "Создана новая рабочая версия." : "Обновлена рабочая версия.";
    }

    private static bool SameContent(ObjectVersion current, ObjectVersion source, IReadOnlyList<Component> components)
    {
        if (current.Name != source.Name || current.Material != source.Material || current.MassKg != source.MassKg
            || current.Components.Count != components.Count) return false;
        var quantities = components.ToDictionary(component => component.ChildObject.Id, component => component.Quantity);
        return current.Components.All(component => quantities.TryGetValue(component.ChildObject.Id, out var quantity)
            && quantity == component.Quantity);
    }

    private static void ValidateComponents(CadDocument document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(document.FileName);
        ArgumentNullException.ThrowIfNull(document.Components);
        if (document.Type != CadDocumentType.Assembly && document.Components.Count > 0)
            throw new PDMException("Компоненты могут быть только у сборок.");
        var names = new HashSet<string>(FileNames);
        foreach (var component in document.Components)
        {
            if (string.IsNullOrWhiteSpace(component.FileName))
                throw new PDMException("Имя файла компонента не может быть пустым.");
            if (component.Quantity <= 0)
                throw new PDMException($"Количество компонента «{component.FileName}» должно быть больше нуля.");
            if (!names.Add(component.FileName))
                throw new PDMException($"Компонент «{component.FileName}» повторяется в составе.");
        }
    }

    private static void RejectDuplicates(IReadOnlyList<CadDocument> documents, Dictionary<string, string> rejected)
    {
        RejectGroups(documents.GroupBy(document => document.FileName, FileNames), "Повторяется имя файла");
        RejectGroups(documents.Where(document => document.Type != CadDocumentType.StandardPart && document.Designation is not null)
            .GroupBy(document => document.Designation!, StringComparer.Ordinal), "Дублируется обозначение");
        // При одинаковых именах стандартных изделий отклоняем все неоднозначные документы.
        RejectGroups(documents.Where(document => document.Type == CadDocumentType.StandardPart)
            .GroupBy(document => document.Name, StringComparer.Ordinal), "Дублируется наименование стандартного изделия");

        void RejectGroups(IEnumerable<IGrouping<string, CadDocument>> groups, string reason)
        {
            foreach (var group in groups.Where(group => group.Count() > 1))
                foreach (var document in group)
                    rejected.TryAdd(document.FileName, $"{reason} «{group.Key}».");
        }
    }

    private static void RejectInvalidDependencies(Dictionary<string, ImportItem> items,
        Dictionary<string, string> rejected, CancellationToken ct)
    {
        var visited = new HashSet<string>(FileNames);
        var active = new HashSet<string>(FileNames);
        var path = new List<string>();
        foreach (var file in items.Keys) Visit(file);

        void Visit(string file)
        {
            ct.ThrowIfCancellationRequested();
            if (rejected.ContainsKey(file) || visited.Contains(file)) return;
            if (!active.Add(file))
            {
                var start = path.FindIndex(name => FileNames.Equals(name, file));
                foreach (var participant in path.Skip(start))
                    rejected.TryAdd(participant, "Обнаружена циклическая зависимость.");
                return;
            }
            path.Add(file);
            foreach (var component in items[file].Document.Components)
            {
                if (items.ContainsKey(component.FileName)) Visit(component.FileName);
                if (!items.ContainsKey(component.FileName) || rejected.ContainsKey(component.FileName))
                {
                    rejected.TryAdd(file, rejected.ContainsKey(component.FileName)
                        ? $"Компонент «{component.FileName}» отклонён."
                        : $"Файл компонента «{component.FileName}» отсутствует.");
                    break;
                }
            }
            path.RemoveAt(path.Count - 1);
            active.Remove(file);
            visited.Add(file);
        }
    }

    private sealed class ImportItem(CadDocument document, PDMObject imported)
    {
        public CadDocument Document { get; } = document;
        public PDMObject Imported { get; } = imported;
        public PDMObject Target { get; set; } = imported;
        public Component[] Components { get; set; } = [];
        public bool HasChanges { get; set; }
        public bool IsNew => ReferenceEquals(Target, Imported);
    }
}
