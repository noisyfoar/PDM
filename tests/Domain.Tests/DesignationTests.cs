using Domain.Exceptions;
using Domain.Objects;

namespace Domain.Tests;

public class DesignationTests
{
    [Theory]
    [InlineData("АБВГ.301245.001")]
    [InlineData("ЁЖЗИ.000000.000")]
    [InlineData("ЯЯЯЯ.999999.999")]
    public void Create_ValidDesignation_PreservesValue(string value)
    {
        var designation = Designation.Create(value);

        Assert.Equal(value, designation.Value);
        Assert.Equal(value, designation.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("АБВ.301245.001")]
    [InlineData("АБВГД.301245.001")]
    [InlineData("АБВГ.30124.001")]
    [InlineData("АБВГ.301245.01")]
    [InlineData("AБВГ.301245.001")]
    [InlineData("абвг.301245.001")]
    [InlineData("АБВГ-301245-001")]
    [InlineData(" АБВГ.301245.001")]
    [InlineData("АБВГ.301245.001 ")]
    public void Create_InvalidDesignation_Throws(string? value)
    {
        Assert.Throws<PDMException>(() => Designation.Create(value));
    }

    [Theory]
    [InlineData("АБВГ.301245.001\n")]
    [InlineData("АБВГ.١٢٣٤٥٦.٧٨٩")]
    [InlineData("АБВГ.１２３４５６.７８９")]
    public void Create_TrailingNewlineOrNonAsciiDigits_Throws(string value)
    {
        Assert.Throws<PDMException>(() => Designation.Create(value));
    }
}
