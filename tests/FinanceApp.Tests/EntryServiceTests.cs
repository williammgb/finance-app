using FinanceApp.Services;

namespace FinanceApp.Tests;

public sealed class EntryServiceTests : IDisposable
{
    private readonly TestDb db = new();

    public void Dispose() => db.Dispose();

    [Fact]
    public async Task ForMonthListsThatMonthByCategoryNameThenId()
    {
        var zoo = await db.Category("Zoo");
        var apples = await db.Category("apples");
        var bikes = await db.Category("Bikes");
        var zooLine = await db.Line(2026, 10, zoo, 500, "tickets");
        var bikesLine = await db.Line(2026, 10, bikes, 300);
        var applesFirst = await db.Line(2026, 10, apples, 200);
        var applesSecond = await db.Line(2026, 10, apples, 100, "market");
        await db.Line(2026, 9, apples, 999);
        await db.Line(2025, 10, apples, 999);

        var lines = await db.Entries.ForMonth(2026, 10);

        Assert.Equal(
            [
                new EntryLine(applesFirst, 2026, 10, apples, "apples", CategoryColors.At(1), 200, null),
                new EntryLine(applesSecond, 2026, 10, apples, "apples", CategoryColors.At(1), 100, "market"),
                new EntryLine(bikesLine, 2026, 10, bikes, "Bikes", CategoryColors.At(2), 300, null),
                new EntryLine(zooLine, 2026, 10, zoo, "Zoo", CategoryColors.At(0), 500, "tickets"),
            ],
            lines);
    }

    [Fact]
    public async Task ForMonthWithoutLinesIsEmpty()
    {
        await db.Category("Food");

        Assert.Empty(await db.Entries.ForMonth(2026, 10));
    }

    [Fact]
    public async Task ZeroAmountIsAllowed()
    {
        var food = await db.Category("Food");

        var result = await db.Entries.Add(2026, 10, food, 0, null);

        Assert.True(result.Ok);
        Assert.Equal(0, Assert.Single(await db.Entries.ForMonth(2026, 10)).AmountCents);
    }

    [Fact]
    public async Task LargestAmountIsAllowed()
    {
        var food = await db.Category("Food");

        Assert.True((await db.Entries.Add(2026, 10, food, 100_000_000, null)).Ok);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000_001)]
    [InlineData(long.MinValue)]
    public async Task AmountOutsideRangeIsRefused(long cents)
    {
        var food = await db.Category("Food");
        var line = await db.Line(2026, 10, food, 1250);

        var added = await db.Entries.Add(2026, 10, food, cents, null);
        var updated = await db.Entries.Update(line, food, cents, null);

        Assert.Equal("Enter an amount like 12,50", added.Error);
        Assert.Equal("Enter an amount like 12,50", updated.Error);
        Assert.Equal(1250, Assert.Single(await db.Entries.ForMonth(2026, 10)).AmountCents);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public async Task MonthOutsideRangeIsRefused(int month)
    {
        var food = await db.Category("Food");

        var result = await db.Entries.Add(2026, month, food, 100, null);

        Assert.Equal("Month must be 1–12", result.Error);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    public async Task YearOutsideRangeIsRefused(int year)
    {
        var food = await db.Category("Food");

        var result = await db.Entries.Add(year, 1, food, 100, null);

        Assert.Equal("Year must be 2000–2100", result.Error);
    }

    [Theory]
    [InlineData(2000, 1)]
    [InlineData(2100, 12)]
    public async Task EdgeMonthsAreAllowed(int year, int month)
    {
        var food = await db.Category("Food");

        Assert.True((await db.Entries.Add(year, month, food, 100, null)).Ok);
        Assert.Single(await db.Entries.ForMonth(year, month));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task NoteOfOnlySpacesIsStoredAsNull(string? note)
    {
        var food = await db.Category("Food");
        var line = await db.Line(2026, 10, food, 100, "first");

        await db.Line(2026, 10, food, 200, note);
        await db.Entries.Update(line, food, 100, note);

        Assert.All(await db.Entries.ForMonth(2026, 10), l => Assert.Null(l.Note));
    }

    [Fact]
    public async Task NoteIsTrimmedAndTwoHundredIsAllowed()
    {
        var food = await db.Category("Food");
        var note = new string('n', 200);

        await db.Line(2026, 10, food, 100, "  " + note + "  ");

        Assert.Equal(note, Assert.Single(await db.Entries.ForMonth(2026, 10)).Note);
    }

    [Fact]
    public async Task NoteTooLongIsRefused()
    {
        var food = await db.Category("Food");
        var line = await db.Line(2026, 10, food, 100, "kept");
        var tooLong = new string('n', 201);

        var added = await db.Entries.Add(2026, 10, food, 100, tooLong);
        var updated = await db.Entries.Update(line, food, 100, tooLong);

        Assert.Equal("Note is too long", added.Error);
        Assert.Equal("Note is too long", updated.Error);
        Assert.Equal("kept", Assert.Single(await db.Entries.ForMonth(2026, 10)).Note);
    }

    [Fact]
    public async Task UnknownCategoryIsRefused()
    {
        var food = await db.Category("Food");
        var line = await db.Line(2026, 10, food, 100);

        Assert.Equal("Category not found", (await db.Entries.Add(2026, 10, 99, 100, null)).Error);
        Assert.Equal("Category not found", (await db.Entries.Update(line, 99, 100, null)).Error);
        Assert.Equal(food, Assert.Single(await db.Entries.ForMonth(2026, 10)).CategoryId);
    }

    [Fact]
    public async Task UnknownLineIsNotFound()
    {
        var food = await db.Category("Food");

        Assert.Equal("Line not found", (await db.Entries.Update(99, food, 100, null)).Error);
        Assert.Equal("Line not found", (await db.Entries.Delete(99)).Error);
    }

    [Fact]
    public async Task UpdateChangesCategoryAmountAndNoteButNotTheMonth()
    {
        var food = await db.Category("Food");
        var pets = await db.Category("Pets");
        var line = await db.Line(2026, 10, food, 100, "old");

        var result = await db.Entries.Update(line, pets, 4500, "dierenarts");

        Assert.True(result.Ok);
        Assert.Equal(
            new EntryLine(line, 2026, 10, pets, "Pets", CategoryColors.At(1), 4500, "dierenarts"),
            Assert.Single(await db.Entries.ForMonth(2026, 10)));
    }

    [Fact]
    public async Task DeleteRemovesOnlyThatLine()
    {
        var food = await db.Category("Food");
        var gone = await db.Line(2026, 10, food, 100);
        var kept = await db.Line(2026, 10, food, 200);

        var result = await db.Entries.Delete(gone);

        Assert.True(result.Ok);
        Assert.Equal(kept, Assert.Single(await db.Entries.ForMonth(2026, 10)).Id);
    }

    [Fact]
    public async Task ManyLinesPerCategoryPerMonth()
    {
        var fun = await db.Category("Entertainment");
        for (var i = 1; i <= 5; i++)
        {
            await db.Line(2026, 10, fun, i * 100);
        }

        var list = await db.Categories.List();

        Assert.Equal(5, (await db.Entries.ForMonth(2026, 10)).Count);
        Assert.Equal(1500, Assert.Single(list).TotalCents);
    }
}
