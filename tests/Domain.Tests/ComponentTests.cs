using Domain.Composition;
using Domain.Exceptions;

namespace Domain.Tests;

public class ComponentTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(int.MaxValue)]
    public void Constructor_PositiveQuantity_PreservesChildAndQuantity(int quantity)
    {
        var child = TestObjects.Part();

        var component = new Component(child, quantity);

        Assert.Same(child, component.ChildObject);
        Assert.Equal(quantity, component.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_NonPositiveQuantity_Throws(int quantity)
    {
        Assert.Throws<ArgumentException>(() => new Component(TestObjects.Part(), quantity));
    }

    [Fact]
    public void Constructor_MissingChild_Throws()
    {
        Assert.Throws<PDMException>(() => new Component(null!, 1));
    }

    [Fact]
    public void ChildObject_NewVersion_IsVisibleThroughExistingComponent()
    {
        var child = TestObjects.Part();
        var component = new Component(child, 2);
        child.CurrentVersion!.Approve();

        var next = child.CreateNewVersion();
        next.Update("Вал после доработки", "Сталь 45", 3m, []);

        Assert.Same(next, component.ChildObject.CurrentVersion);
        Assert.Equal(3m, component.ChildObject.CurrentVersion!.MassKg);
        Assert.Equal(2, component.Quantity);
    }
}
