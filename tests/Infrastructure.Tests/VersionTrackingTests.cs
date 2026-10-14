using Domain.Components;
using Domain.Objects;
using Domain.Versions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Infrastructure.Tests;

public sealed class VersionTrackingTests
{
    // Проверяется только модель и ChangeTracker. Подключений, запросов и SaveChanges нет.
    private static PdmDbContext Context() => new(new DbContextOptionsBuilder<PdmDbContext>()
        .UseNpgsql("Host=localhost;Port=1;Database=version_tracking_test;Username=test;Password=test").Options);

    [Fact]
    public void NewVersionIsTrackedAsInsertWhileApprovedVersionAndItsLinksStayUnchanged()
    {
        var child = new PDMObject(ObjectType.Part, Designation.Create("АБВГ.123456.001"), "Деталь", "Сталь", 2m);
        var assembly = new PDMObject(ObjectType.Assembly, Designation.Create("АБВГ.123456.002"), "Сборка",
            components: [new(child, 2)]);
        var previous = assembly.CurrentVersion!;
        previous.Approve();
        var previousLink = Assert.Single(previous.Components);
        using var context = Context();
        context.Add(assembly);
        context.ChangeTracker.AcceptAllChanges();

        var current = assembly.CreateNewVersion();
        current.Update("Сборка новая", null, null, [new(child, 5)]);
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, context.Entry(current).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(previous).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(previousLink).State);
        Assert.Equal(ObjectVersionType.Approved, previous.VersionType);
        Assert.Equal(2, previousLink.Quantity);
        var newLink = Assert.Single(current.Components);
        Assert.Equal(EntityState.Added, context.Entry(newLink).State);
        Assert.Equal(current.Id, context.Entry(newLink).Property<Guid>("ParentVersionId").CurrentValue);
        Assert.Equal(child.Id, context.Entry(newLink).Property<Guid>("ChildObjectId").CurrentValue);
        Assert.Equal(assembly.Id, context.Entry(current).Property<Guid>("ObjectId").CurrentValue);
        Assert.Equal(2, current.VersionNumber);
    }

    [Fact]
    public void WorkingVersionKeepsItsIdAndReplacesLinksWithoutAddingAnotherVersion()
    {
        var child = new PDMObject(ObjectType.Part, Designation.Create("АБВГ.123456.001"), "Деталь", "Сталь", 2m);
        var assembly = new PDMObject(ObjectType.Assembly, Designation.Create("АБВГ.123456.002"), "Сборка",
            components: [new(child, 2)]);
        var version = assembly.CurrentVersion!;
        var id = version.Id;
        var previousLink = Assert.Single(version.Components);
        using var context = Context();
        context.Add(assembly);
        context.ChangeTracker.AcceptAllChanges();

        version.Update("Сборка обновлённая", null, null, [new(child, 5)]);
        context.ChangeTracker.DetectChanges();

        Assert.Equal(id, Assert.Single(assembly.Versions).Id);
        Assert.Equal(EntityState.Modified, context.Entry(version).State);
        Assert.Equal(EntityState.Deleted, context.Entry(previousLink).State);
        Assert.Equal(EntityState.Added, context.Entry(Assert.Single(version.Components)).State);
        Assert.DoesNotContain(context.ChangeTracker.Entries<ObjectVersion>(), entry => entry.State == EntityState.Added);
    }
}
