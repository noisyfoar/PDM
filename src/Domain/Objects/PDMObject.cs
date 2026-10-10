using Domain.Composition;
using Domain.Exceptions;
using Domain.Versions;

namespace Domain.Objects;

public sealed class PDMObject
{
    public Guid Id { get; }
    public ObjectType ObjectType { get; }
    public Designation? Designation { get; }
    private readonly List<ObjectVersion> _versions = [];
    public IReadOnlyList<ObjectVersion> Versions => _versions.OrderBy(version => version.VersionNumber).ToList().AsReadOnly(); // сортируем версии по возрастанию номера версии 
    public ObjectVersion? CurrentVersion => _versions
        .Where(version => version.VersionType != ObjectVersionType.Annulled)
        .MaxBy(version => version.VersionNumber);
    public string Name => (CurrentVersion ?? _versions.MaxBy(version => version.VersionNumber)!).Name; // если текущей версии нет, берем последнюю версию

    // EF Core заполняет свойства и навигации из БД через закрытые поля.
    private PDMObject() { }

    public PDMObject(
        ObjectType objectType,
        Designation? designation,
        string name,
        string? material = null,
        decimal? massKg = null,
        IReadOnlyList<Component>? components = null)
    {
        if (!Enum.IsDefined(objectType))
            throw new PDMException("Неизвестный тип объекта.");

        if (objectType == ObjectType.StandardPart && designation is not null) // стандартное изделие не может иметь обозначение
        {
            throw new PDMException(
                "Стандартное изделие не может иметь обозначение.");
        }

        if (objectType != ObjectType.StandardPart && designation is null) // для сборок и деталей обозначение обязательно
        {
            throw new PDMException(
                "Для сборок и деталей необходимо обозначение.");
        }

        Id = Guid.NewGuid();
        ObjectType = objectType;
        // Обозначение — значение: у каждого объекта свой экземпляр с тем же текстом.
        Designation = designation is null ? null : Designation.Create(designation.Value);
        _versions = 
        [ 
            new ObjectVersion(this, 1, name, material, massKg, components ?? []), // создаем первую версию объекта
        ];
    }

    public ObjectVersion CreateNewVersion()
    {
        if (CurrentVersion?.VersionType == ObjectVersionType.InWork)
            throw new PDMException("Версия в состоянии «В работе» уже существует.");

        var latest = _versions.MaxBy(version => version.VersionNumber)!; // последняя версия объекта
        if (latest.VersionNumber == int.MaxValue)
            throw new PDMException("Достигнут предел номера версии.");

        var source = CurrentVersion ?? latest;

        var version = new ObjectVersion(
            this,
            latest.VersionNumber + 1,
            source.Name,
            source.Material,
            source.MassKg,
            [.. source.Components.Select(component => new Component(component.ChildObject, component.Quantity))]); // создаем новую версию объекта с теми же компонентами, что и у исходной версии

        _versions.Add(version);

        return version;
    }
}
