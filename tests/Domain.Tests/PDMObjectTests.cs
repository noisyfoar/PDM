using Domain.Composition;
using Domain.Exceptions;
using Domain.Objects;
using Domain.Versions;

namespace Domain.Tests;

public class PDMObjectTests
{
    [Fact]
    public void Constructor_CreatesFirstWorkingVersionOwnedByObject()
    {
        var item = TestObjects.Part();
        var version = Assert.Single(item.Versions);

        Assert.Same(item, version.Owner);
        Assert.Same(version, item.CurrentVersion);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal(ObjectVersionType.InWork, version.VersionType);
        Assert.Equal("Вал", item.Name);
    }

    [Theory]
    [InlineData(ObjectType.Assembly)]
    [InlineData(ObjectType.Part)]
    public void Constructor_AssemblyOrPartWithoutDesignation_Throws(ObjectType type)
    {
        Assert.Throws<PDMException>(() => new PDMObject(type, null, "Объект"));
    }

    [Fact]
    public void Constructor_StandardPartWithDesignation_Throws()
    {
        Assert.Throws<PDMException>(() => new PDMObject(
            ObjectType.StandardPart, Designation.Create("АБВГ.301245.001"),
            "Болт", massKg: 0.1m));
    }

    [Fact]
    public void Constructor_StandardPartWithoutDesignation_IsAllowed()
    {
        var item = new PDMObject(ObjectType.StandardPart, null, "Болт", massKg: 0.1m);

        Assert.Null(item.Designation);
        Assert.Equal("Болт", item.Name);
        Assert.Equal(0.1m, item.CurrentVersion!.MassKg);
    }

    [Fact]
    public void Constructor_UnknownObjectType_Throws()
    {
        Assert.Throws<PDMException>(() => new PDMObject(
            (ObjectType)999, Designation.Create("АБВГ.301245.001"), "Объект"));
    }

    [Fact]
    public void CreateNewVersion_ExistingWorkingVersion_ThrowsWithoutChangingHistory()
    {
        var item = TestObjects.Part();
        var original = item.CurrentVersion;

        Assert.Throws<PDMException>(() => item.CreateNewVersion());

        Assert.Same(original, Assert.Single(item.Versions));
        Assert.Same(original, item.CurrentVersion);
    }

    [Fact]
    public void CreateNewVersion_ApprovedVersion_CopiesAttributesIntoWorkingVersion()
    {
        var item = TestObjects.Part();
        var first = item.CurrentVersion!;
        first.Approve();

        var second = item.CreateNewVersion();

        Assert.NotEqual(first.Id, second.Id);
        Assert.Same(item, second.Owner);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(ObjectVersionType.InWork, second.VersionType);
        Assert.Equal(first.Name, second.Name);
        Assert.Equal(first.Material, second.Material);
        Assert.Equal(first.MassKg, second.MassKg);
        Assert.Same(second, item.CurrentVersion);
        Assert.Equal(ObjectVersionType.Approved, first.VersionType);
    }

    [Fact]
    public void AnnulLatestVersion_RestoresPreviousCurrentVersionAndName()
    {
        var item = TestObjects.Part();
        var first = item.CurrentVersion!;
        first.Approve();
        var second = item.CreateNewVersion();
        second.Update("Новое имя", "Сталь 45", 3m, []);

        second.Annul();

        Assert.Same(first, item.CurrentVersion);
        Assert.Equal("Вал", item.Name);
        Assert.Equal(2, item.Versions.Count);
    }

    [Fact]
    public void AnnulAllVersions_CurrentVersionIsMissingButLastNameIsAvailable()
    {
        var item = TestObjects.Part();
        item.CurrentVersion!.Approve();
        var second = item.CreateNewVersion();
        second.Update("Последнее имя", "Сталь 45", 3m, []);

        item.Versions[0].Annul();
        second.Annul();

        Assert.Null(item.CurrentVersion);
        Assert.Equal("Последнее имя", item.Name);
    }

    [Fact]
    public void CreateNewVersion_AllVersionsAnnulled_CopiesLastVersion()
    {
        var item = TestObjects.Part();
        item.CurrentVersion!.Annul();

        var second = item.CreateNewVersion();

        Assert.Equal(2, second.VersionNumber);
        Assert.Equal("Вал", second.Name);
        Assert.Equal("Сталь 40Х", second.Material);
        Assert.Equal(2m, second.MassKg);
        Assert.Equal(ObjectVersionType.InWork, second.VersionType);
        Assert.Same(second, item.CurrentVersion);
    }

    [Fact]
    public void CreateNewVersion_LatestAnnulled_UsesCurrentAttributesWithoutReusingNumber()
    {
        var item = TestObjects.Part();
        item.CurrentVersion!.Approve();
        var second = item.CreateNewVersion();
        second.Update("Отменённое изменение", "Сталь 45", 3m, []);
        second.Annul();

        var third = item.CreateNewVersion();

        Assert.Equal(3, third.VersionNumber);
        Assert.Equal("Вал", third.Name);
        Assert.Equal("Сталь 40Х", third.Material);
        Assert.Equal(2m, third.MassKg);
        Assert.Equal(3, item.Versions.Count);
    }

    [Fact]
    public void Versions_ExternalClearIsBlockedAndHistoryIsPreserved()
    {
        var item = TestObjects.Part();
        var current = item.CurrentVersion;

        if (item.Versions is IList<ObjectVersion> list)
            Assert.Throws<NotSupportedException>(() => list.Clear());

        Assert.Same(current, Assert.Single(item.Versions));
        Assert.Same(current, item.CurrentVersion);
        Assert.Equal("Вал", item.Name);
    }

    [Fact]
    public void CreateNewVersion_ChangingNewComposition_PreservesApprovedComposition()
    {
        var child = TestObjects.Part();
        var item = TestObjects.Assembly([new Component(child, 2)]);
        var first = item.CurrentVersion!;
        first.Approve();

        var second = item.CreateNewVersion();
        var copied = Assert.Single(second.Components);
        Assert.Same(child, copied.ChildObject);
        Assert.Equal(2, copied.Quantity);

        var changed = new List<Component> { new(child, 5) };
        second.Update("Сборка после доработки", null, null, changed);
        changed.Clear();

        Assert.Equal(2, Assert.Single(first.Components).Quantity);
        Assert.Equal(5, Assert.Single(second.Components).Quantity);
        Assert.Equal("Сборка", first.Name);
        Assert.Equal(ObjectVersionType.Approved, first.VersionType);
    }
}
