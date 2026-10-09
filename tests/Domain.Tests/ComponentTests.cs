using Domain.Composition;

namespace Domain.Tests;

public class ComponentTests
{
    [Fact]
    public void Constructor_WithZeroQuantity_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Component(Guid.NewGuid(), 0));
    }
}
