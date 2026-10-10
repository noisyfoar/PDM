using Domain.Composition;
using Domain.Exceptions;
using Domain.Objects;
using Domain.Versions;

namespace Domain.Tests;

public class ObjectVersionTests
{
    [Fact]
    public void Update_WorkingVersion_ChangesAttributesWithoutCreatingAnotherVersion()
    {
        var item = TestObjects.Part();
        var version = item.CurrentVersion!;
        var id = version.Id;
        var createdAt = version.CreatedAt;

        version.Update("Вал после доработки", "Сталь 45", 3m, []);

        Assert.Equal("Вал после доработки", item.Name);
        Assert.Equal("Сталь 45", version.Material);
        Assert.Equal(3m, version.MassKg);
        Assert.Equal(id, version.Id);
        Assert.Equal(createdAt, version.CreatedAt);
        Assert.Same(version, Assert.Single(item.Versions));
    }

    [Fact]
    public void Update_PartWithoutMass_IsAllowed()
    {
        var version = TestObjects.Part().CurrentVersion!;

        version.Update("Вал", "Сталь 40Х", null, []);

        Assert.Null(version.MassKg);
    }

    [Fact]
    public void Approve_WorkingVersion_ChangesState()
    {
        var version = TestObjects.Part().CurrentVersion!;

        version.Approve();

        Assert.Equal(ObjectVersionType.Approved, version.VersionType);
    }

    [Theory]
    [InlineData(ObjectVersionType.Approved)]
    [InlineData(ObjectVersionType.Annulled)]
    public void Approve_NonWorkingVersion_ThrowsAndPreservesState(ObjectVersionType state)
    {
        var version = CreateVersionInState(state);

        Assert.Throws<PDMException>(() => version.Approve());

        Assert.Equal(state, version.VersionType);
    }

    [Theory]
    [InlineData(ObjectVersionType.InWork)]
    [InlineData(ObjectVersionType.Approved)]
    public void Annul_WorkingOrApprovedVersion_ChangesState(ObjectVersionType state)
    {
        var version = CreateVersionInState(state);

        version.Annul();

        Assert.Equal(ObjectVersionType.Annulled, version.VersionType);
        Assert.Null(version.Owner.CurrentVersion);
    }

    [Fact]
    public void Annul_AlreadyAnnulledVersion_Throws()
    {
        var version = CreateVersionInState(ObjectVersionType.Annulled);

        Assert.Throws<PDMException>(() => version.Annul());

        Assert.Equal(ObjectVersionType.Annulled, version.VersionType);
    }

    [Theory]
    [InlineData(ObjectVersionType.Approved)]
    [InlineData(ObjectVersionType.Annulled)]
    public void Update_NonWorkingVersion_ThrowsWithoutChangingAttributes(ObjectVersionType state)
    {
        var version = CreateVersionInState(state);

        Assert.Throws<PDMException>(() => version.Update("Изменение", "Сталь 45", 3m, []));

        Assert.Equal("Вал", version.Name);
        Assert.Equal("Сталь 40Х", version.Material);
        Assert.Equal(2m, version.MassKg);
        Assert.Equal(state, version.VersionType);
    }

    [Theory]
    [InlineData(ObjectVersionType.Approved)]
    [InlineData(ObjectVersionType.Annulled)]
    public void Update_NonWorkingAssembly_ThrowsWithoutChangingComposition(ObjectVersionType state)
    {
        var child = TestObjects.Part();
        var version = TestObjects.Assembly([new Component(child, 2)]).CurrentVersion!;
        SetState(version, state);

        Assert.Throws<PDMException>(() => version.Update("Изменение", null, null, []));

        Assert.Equal("Сборка", version.Name);
        Assert.Equal(2, Assert.Single(version.Components).Quantity);
        Assert.Equal(state, version.VersionType);
    }

    [Fact]
    public void Constructor_ApprovedAssembly_PreservesCompositionAfterSourceListChanges()
    {
        var child = TestObjects.Part();
        var component = new Component(child, 2);
        var source = new List<Component> { component };
        var version = TestObjects.Assembly(source).CurrentVersion!;
        version.Approve();

        source.Clear();
        source.Add(new Component(child, 10));

        Assert.Same(component, Assert.Single(version.Components));
        Assert.Equal(2, version.Components[0].Quantity);
        Assert.Same(child, version.Components[0].ChildObject);
    }

    [Fact]
    public void Update_PreservesCompositionAfterSourceArrayChanges()
    {
        var child = TestObjects.Part();
        var original = new Component(child, 2);
        var source = new[] { original };
        var version = TestObjects.Assembly().CurrentVersion!;
        version.Update("Сборка", null, null, source);

        source[0] = new Component(child, 10);

        Assert.Same(original, Assert.Single(version.Components));
        Assert.Equal(2, version.Components[0].Quantity);
    }

    [Fact]
    public void Components_ExternalReplacementIsBlocked()
    {
        var child = TestObjects.Part();
        var component = new Component(child, 2);
        var version = TestObjects.Assembly([component]).CurrentVersion!;
        version.Approve();

        if (version.Components is IList<Component> list)
        {
            Assert.Throws<NotSupportedException>(() => list[0] = new Component(child, 10));
            Assert.Throws<NotSupportedException>(() => list.Clear());
        }

        Assert.Same(component, Assert.Single(version.Components));
    }

    [Fact]
    public void Constructor_NegativeMass_Throws()
    {
        Assert.Throws<PDMException>(() => TestObjects.Part(massKg: -1m));
    }

    [Fact]
    public void Update_NegativeMass_ThrowsWithoutChangingAttributes()
    {
        var version = TestObjects.Part().CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update("Изменение", "Сталь 45", -1m, []));

        Assert.Equal("Вал", version.Name);
        Assert.Equal("Сталь 40Х", version.Material);
        Assert.Equal(2m, version.MassKg);
    }

    [Fact]
    public void Update_WithOwnComponents_PreservesComposition()
    {
        var component = new Component(TestObjects.Part(), 2);
        var version = TestObjects.Assembly([component]).CurrentVersion!;

        version.Update("Новое имя сборки", null, null, version.Components);

        Assert.Equal("Новое имя сборки", version.Name);
        Assert.Same(component, Assert.Single(version.Components));
    }

    [Fact]
    public void Update_NullComponents_ThrowsWithoutChangingVersion()
    {
        var component = new Component(TestObjects.Part(), 2);
        var version = TestObjects.Assembly([component]).CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update("Изменение", null, null, null!));

        Assert.Equal("Сборка", version.Name);
        Assert.Same(component, Assert.Single(version.Components));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Update_EmptyName_ThrowsWithoutChangingVersion(string? name)
    {
        var version = TestObjects.Part().CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update(name!, "Сталь 45", 3m, []));

        Assert.Equal("Вал", version.Name);
        Assert.Equal("Сталь 40Х", version.Material);
        Assert.Equal(2m, version.MassKg);
    }

    [Theory]
    [InlineData(ObjectType.Assembly, "Сталь", false)]
    [InlineData(ObjectType.Assembly, null, true)]
    [InlineData(ObjectType.Part, null, true)]
    [InlineData(ObjectType.Part, "", true)]
    [InlineData(ObjectType.Part, " ", true)]
    [InlineData(ObjectType.StandardPart, "Сталь", true)]
    [InlineData(ObjectType.StandardPart, null, false)]
    public void Constructor_AttributesInvalidForObjectType_Throws(
        ObjectType type, string? material, bool hasMass)
    {
        var designation = type == ObjectType.StandardPart
            ? null
            : Designation.Create("АБВГ.301245.001");

        Assert.Throws<PDMException>(() => new PDMObject(
            type, designation, "Объект", material, hasMass ? 1m : null));
    }

    [Theory]
    [InlineData(ObjectType.Part)]
    [InlineData(ObjectType.StandardPart)]
    public void Update_NonAssemblyWithComponents_ThrowsWithoutChangingVersion(ObjectType type)
    {
        var item = type == ObjectType.Part
            ? TestObjects.Part()
            : new PDMObject(ObjectType.StandardPart, null, "Болт", massKg: 0.1m);
        var version = item.CurrentVersion!;
        var originalMass = version.MassKg;

        Assert.Throws<PDMException>(() => version.Update(
            "Изменение", version.Material, originalMass,
            [new Component(TestObjects.Part(), 2)]));

        Assert.Equal(type == ObjectType.Part ? "Вал" : "Болт", version.Name);
        Assert.Equal(originalMass, version.MassKg);
        Assert.Empty(version.Components);
    }

    [Fact]
    public void Update_DuplicateComponents_ThrowsWithoutChangingVersion()
    {
        var child = TestObjects.Part();
        var original = new Component(child, 2);
        var version = TestObjects.Assembly([original]).CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update(
            "Изменение", null, null, [new Component(child, 3), new Component(child, 4)]));

        Assert.Equal("Сборка", version.Name);
        Assert.Same(original, Assert.Single(version.Components));
    }

    [Fact]
    public void Update_SelfReference_ThrowsWithoutChangingVersion()
    {
        var original = new Component(TestObjects.Part(), 2);
        var item = TestObjects.Assembly([original]);
        var version = item.CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update(
            "Изменение", null, null, [new Component(item, 1)]));

        Assert.Equal("Сборка", version.Name);
        Assert.Same(original, Assert.Single(version.Components));
    }

    [Fact]
    public void Update_NullComponent_ThrowsWithoutChangingVersion()
    {
        var original = new Component(TestObjects.Part(), 2);
        var version = TestObjects.Assembly([original]).CurrentVersion!;

        Assert.Throws<PDMException>(() => version.Update("Изменение", null, null, [null!]));

        Assert.Equal("Сборка", version.Name);
        Assert.Same(original, Assert.Single(version.Components));
    }

    private static ObjectVersion CreateVersionInState(ObjectVersionType state)
    {
        var version = TestObjects.Part().CurrentVersion!;
        SetState(version, state);
        return version;
    }

    private static void SetState(ObjectVersion version, ObjectVersionType state)
    {
        switch (state)
        {
            case ObjectVersionType.InWork:
                break;
            case ObjectVersionType.Approved:
                version.Approve();
                break;
            case ObjectVersionType.Annulled:
                version.Annul();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }
    }
}
