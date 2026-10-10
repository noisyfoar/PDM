using Domain.Components;
using Domain.Exceptions;
using Domain.Objects;

namespace Domain.Versions;

public sealed class ObjectVersion
{
    public Guid Id { get; }
    public PDMObject Owner { get; }
    public ObjectVersionType VersionType { get; private set; } = ObjectVersionType.InWork;
    public int VersionNumber { get; }
    public string Name { get; private set; } = string.Empty;
    public string? Material { get; private set; }
    public decimal? MassKg { get; private set; }
    private readonly List<Component> _components = [];
    public IReadOnlyList<Component> Components => _components.AsReadOnly();
    public DateTime CreatedAt { get; }

    // EF Core заполняет свойства и навигации из БД через закрытые поля.
    private ObjectVersion() => Owner = null!;

    internal ObjectVersion(PDMObject owner, int versionNumber, string name, string? material, decimal? massKg, IReadOnlyList<Component> components)
    {
        if (owner is null)
            throw new PDMException("Объект-владелец версии не может отсутствовать.");

        if (versionNumber < 1)
            throw new PDMException("Номер версии должен быть больше нуля.");

        Id = Guid.NewGuid();
        Owner = owner;
        VersionNumber = versionNumber;
        CreatedAt = DateTime.UtcNow;

        Update(name, material, massKg, components);
    }

    public void Update(
        string name,
        string? material,
        decimal? massKg,
        IReadOnlyList<Component> components)
    {
        EnsureInWork();

        if (string.IsNullOrWhiteSpace(name))
            throw new PDMException("Наименование не может быть пустым.");

        if (massKg < 0)
            throw new PDMException("Масса не может быть отрицательной.");

        if (Owner.ObjectType == ObjectType.Part && string.IsNullOrWhiteSpace(material))
            throw new PDMException("Для детали необходимо указать материал.");

        if (Owner.ObjectType != ObjectType.Part && material is not null)
            throw new PDMException("Материал можно указывать только для деталей.");

        if (Owner.ObjectType == ObjectType.Assembly && massKg is not null)
            throw new PDMException("Масса сборки должна вычисляться по её составу.");

        if (Owner.ObjectType == ObjectType.StandardPart && massKg is null)
            throw new PDMException("Для стандартного изделия необходимо указать массу.");

        if (components is null)
            throw new PDMException("Список компонентов не может отсутствовать.");

        // Снимок создаётся до изменения полей и очистки текущего состава.
        var snapshot = components.ToArray();

        if (Owner.ObjectType != ObjectType.Assembly && snapshot.Length > 0)
            throw new PDMException("Компоненты могут быть только у сборок.");

        var childIds = new HashSet<Guid>();
        foreach (var component in snapshot)
        {
            if (component is null)
                throw new PDMException("Компонент не может отсутствовать.");

            if (component.ChildObject.Id == Owner.Id)
                throw new PDMException("Сборка не может содержать саму себя.");

            if (!childIds.Add(component.ChildObject.Id))
                throw new PDMException("Один и тот же компонент не может повторяться в составе.");
        }

        Name = name;
        Material = material;
        MassKg = massKg;
        _components.Clear();
        _components.AddRange(snapshot); // создаем копию списка компонентов, чтобы избежать внешних изменений
    }
    public void Approve()
    {
        EnsureInWork();
        VersionType = ObjectVersionType.Approved;
    }

    public void Annul()
    {
        if (VersionType == ObjectVersionType.Annulled)
            throw new PDMException("Версия уже аннулирована.");

        VersionType = ObjectVersionType.Annulled;
    }

    private void EnsureInWork()
    {
        if (VersionType != ObjectVersionType.InWork)
            throw new PDMException("Изменять и утверждать можно только версии в состоянии «В работе».");
    }

}
