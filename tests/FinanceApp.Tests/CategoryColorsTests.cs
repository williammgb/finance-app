using FinanceApp.Services;

namespace FinanceApp.Tests;

public class CategoryColorsTests
{
    [Theory]
    [InlineData(0, "#2a78d6")]
    [InlineData(1, "#eb6834")]
    [InlineData(11, "#8a9a00")]
    [InlineData(12, "#2a78d6")]
    [InlineData(23, "#8a9a00")]
    public void AtGivesTheColourForAPosition(int position, string expected)
    {
        Assert.Equal(expected, CategoryColors.At(position));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AtRefusesANegativePosition(int position)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CategoryColors.At(position));
    }

    [Fact]
    public void TotalIsInk()
    {
        Assert.Equal("#1c1c1a", CategoryColors.Total);
    }
}
