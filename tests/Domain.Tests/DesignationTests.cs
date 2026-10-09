using Domain.Objects;

namespace Domain.Tests;

public class DesignationTests
{
    [Fact]
    public void Create_WithValidEskdDesignation_ReturnsDesignation()
    {
        var designation = Designation.Create("АААА.123123.123");

        Assert.Equal("АААА.123123.123", designation.Value);
    }

    [Theory]
    [InlineData("ААА.123123.123")]
    [InlineData("АААА.12312.123")]
    [InlineData("ААAA.123123.123")]
    public void Create_WithInvalidDesignation_Throws(string value)
    {
        Assert.Throws<ArgumentException>(() => Designation.Create(value));
    }
}
