using FinanceApp.Services;

namespace FinanceApp.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData("12,50", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData("7.5", 750)]
    [InlineData("12", 1200)]
    [InlineData(" 3,10 ", 310)]
    [InlineData("0", 0)]
    [InlineData("1000000", 100000000)]
    [InlineData("12,05", 1205)]
    [InlineData("1000000,00", 100000000)]
    public void TryParseAcceptsTypedAmounts(string text, long expected)
    {
        Assert.True(Money.TryParse(text, out var cents));
        Assert.Equal(expected, cents);
    }

    [Theory]
    [InlineData("12,5,0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1.234,56")]
    [InlineData("1.234")]
    [InlineData("12,505")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("1000000,01")]
    [InlineData("+5")]
    [InlineData("   ")]
    [InlineData("12,")]
    [InlineData(",50")]
    [InlineData("99999999999999999999999")]
    [InlineData("1000001")]
    public void TryParseRefusesOtherText(string? text)
    {
        Assert.False(Money.TryParse(text, out _));
    }

    [Fact]
    public void ParseErrorTellsWhatToType()
    {
        Assert.Equal("Enter an amount like 12,50", Money.ParseError);
    }

    [Theory]
    [InlineData(0, "€ 0,00")]
    [InlineData(123456, "€ 1.234,56")]
    [InlineData(100000000, "€ 1.000.000,00")]
    [InlineData(-1200, "-€ 12,00")]
    [InlineData(5, "€ 0,05")]
    public void FormatShowsEuros(long cents, string expected)
    {
        Assert.Equal(expected, Money.Format(cents));
    }

    [Theory]
    [InlineData(8900, "+ € 89,00")]
    [InlineData(-1200, "- € 12,00")]
    [InlineData(0, "€ 0,00")]
    public void FormatChangeShowsTheSign(long cents, string expected)
    {
        Assert.Equal(expected, Money.FormatChange(cents));
    }

    [Theory]
    [InlineData(123456, "1234,56")]
    [InlineData(0, "0,00")]
    [InlineData(100000000, "1000000,00")]
    [InlineData(750, "7,50")]
    public void ToInputWritesWhatAnEditFieldShows(long cents, string expected)
    {
        Assert.Equal(expected, Money.ToInput(cents));
    }

    [Fact]
    public void ToInputThenTryParseRoundTripsSeededRandomAmounts()
    {
        var random = new Random(20261022);
        var amounts = new List<long> { 0, 1, 99, 100, 100_000_000 };
        for (var i = 0; i < 1000; i++)
        {
            amounts.Add(random.NextInt64(0, 100_000_001));
        }

        foreach (var cents in amounts)
        {
            var input = Money.ToInput(cents);
            Assert.True(Money.TryParse(input, out var back), $"{cents} wrote {input}, which did not parse");
            Assert.Equal(cents, back);
        }
    }
}
