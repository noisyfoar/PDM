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
    public IReadOnlyList<ObjectVersion> Versions => _versions.AsReadOnly();
    public ObjectVersion? CurrentVersion => _versions.LastOrDefault(version => version.VersionType != ObjectVersionType.Annulled); // текущая версия - последняя неаннулированная версия
    public string Name => (CurrentVersion ?? _versions[^1]).Name; // если текущей версии нет, берем последнюю версию

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
        Designation = designation;
        _versions = 
        [ 
            new ObjectVersion(this, 1, name, material, massKg, components ?? []), // создаем первую версию объекта
        ];
    }

    public ObjectVersion CreateNewVersion()
    {
        if (CurrentVersion?.VersionType == ObjectVersionType.InWork)
            throw new PDMException("Версия в состоянии «В работе» уже существует.");

        if (_versions[^1].VersionNumber == int.MaxValue)
            throw new PDMException("Достигнут предел номера версии.");

        var source = CurrentVersion ?? _versions[^1]; // если текущей версии нет, берем последнюю версию

        var version = new ObjectVersion(
            this,
            _versions[^1].VersionNumber + 1, // номер новой версии на 1 больше, чем у последней версии
            source.Name,
            source.Material,
            source.MassKg,
            source.Components);

        _versions.Add(version);

        return version;
    }
}
