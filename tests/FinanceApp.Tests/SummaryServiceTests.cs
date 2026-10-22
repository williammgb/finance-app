using FinanceApp.Services;

namespace FinanceApp.Tests;

public sealed class SummaryServiceTests : IDisposable
{
    private readonly TestDb db = new();

    public SummaryServiceTests()
    {
        Summary = new SummaryService(db.Factory);
    }

    private SummaryService Summary { get; }

    public void Dispose() => db.Dispose();

    private async Task<Overview> Load(Period period = Period.TwelveMonths)
    {
        var overview = await Summary.Load(period);
        Assert.NotNull(overview);
        return overview;
    }

    [Fact]
    public async Task EmptyDatabaseHasNoOverview()
    {
        Assert.Null(await Summary.Load(Period.TwelveMonths));
        await db.Category("Rent");
        Assert.Null(await Summary.Load(Period.TwelveMonths));
        Assert.Null(await Summary.Load(Period.AllTime));
    }

    [Fact]
    public async Task WindowEndsAtLatestMonthWithData()
    {
        var rent = await db.Category("Rent");
        await db.Line(2024, 3, rent, 99_900);
        await db.Line(2025, 6, rent, 100);
        await db.Line(2026, 5, rent, 200);

        var overview = await Load();

        Assert.Equal(new YearMonth(2026, 5), overview.Latest);
        Assert.Equal(new YearMonth(2024, 3), overview.First);
        Assert.Equal(12, overview.Months.Count);
        Assert.Equal("Jun 25", overview.Months[0].Month.Label);
        Assert.Equal("May 26", overview.Months[^1].Month.Label);
        Assert.Equal(300, overview.Months.Sum(m => m.TotalCents));
        Assert.Equal(300, overview.Categories.Single().TotalCents);
    }

    [Fact]
    public async Task GapMonthsAreZeroOnEveryLine()
    {
        var rent = await db.Category("Rent");
        var food = await db.Category("Food");
        await db.Line(2026, 1, rent, 1000);
        await db.Line(2026, 1, food, 300);
        await db.Line(2026, 3, rent, 1000);

        var overview = await Load();

        Assert.Equal(["Jan 26", "Feb 26", "Mar 26"], overview.Months.Select(m => m.Month.Label));
        Assert.Equal([1300L, 0L, 1000L], overview.Months.Select(m => m.TotalCents));
        Assert.Equal([1000L, 0L, 1000L], overview.Months.Select(m => m.CentsOf(rent)));
        Assert.Equal([300L, 0L, 0L], overview.Months.Select(m => m.CentsOf(food)));
    }

    [Fact]
    public async Task AllTimeRunsFromTheFirstMonth()
    {
        var rent = await db.Category("Rent");
        var other = await db.Category("Other");
        await db.Line(2024, 11, rent, 500);
        await db.Line(2025, 6, other, 50);
        await db.Line(2026, 10, rent, 700);

        var twelve = await Load(Period.TwelveMonths);
        var all = await Load(Period.AllTime);

        Assert.Equal(24, all.Months.Count);
        Assert.Equal(new YearMonth(2024, 11), all.Months[0].Month);
        Assert.Equal(new YearMonth(2026, 10), all.Months[^1].Month);
        Assert.Equal(1250, all.Months.Sum(m => m.TotalCents));
        Assert.Equal(new YearMonth(2025, 11), twelve.Months[0].Month);

        // Categories are ordered by their total in the shown months.
        Assert.Equal(["Rent", "Other"], all.Categories.Select(c => c.Name));
        Assert.Equal([1200L, 50L], all.Categories.Select(c => c.TotalCents));
        Assert.Equal([700L, 0L], twelve.Categories.Select(c => c.TotalCents));

        // The tiles and the donut do not depend on the period.
        Assert.Equal(twelve.AverageCents, all.AverageCents);
        Assert.Equal(twelve.Slices, all.Slices);
    }

    [Fact]
    public async Task WindowCrossesTheYearInOrder()
    {
        var rent = await db.Category("Rent");
        await db.Line(2026, 2, rent, 400);
        await db.Line(2025, 11, rent, 100);

        var overview = await Load();

        Assert.Equal(["Nov 25", "Dec 25", "Jan 26", "Feb 26"], overview.Months.Select(m => m.Month.Label));
        Assert.Equal(["November 2025", "December 2025", "January 2026", "February 2026"], overview.Months.Select(m => m.Month.Title));
        Assert.Equal(new YearMonth(2026, 1), overview.Versus?.Month);
    }

    [Fact]
    public async Task DonutLeavesOutZeroAndOrdersLargestFirst()
    {
        var rent = await db.Category("Rent");
        var food = await db.Category("Food");
        var gifts = await db.Category("Gifts");
        var sport = await db.Category("Sport");
        await db.Line(2026, 10, food, 200);
        await db.Line(2026, 10, food, 100);
        await db.Line(2026, 10, rent, 700);
        await db.Line(2026, 10, gifts, 0);
        await db.Line(2026, 9, sport, 5000);

        var overview = await Load();

        Assert.Equal(
            [
                new DonutSlice("Rent", CategoryColors.At(0), 700, 0.7m),
                new DonutSlice("Food", CategoryColors.At(1), 300, 0.3m),
            ],
            overview.Slices);
        Assert.Equal(1000, overview.LatestCents);
        Assert.Equal("70,0%", SummaryService.FormatShare(overview.Slices[0].Share));
    }

    [Fact]
    public async Task DonutIsEmptyWhenTheLatestMonthIsAllZero()
    {
        var rent = await db.Category("Rent");
        await db.Line(2026, 9, rent, 500);
        await db.Line(2026, 10, rent, 0);

        var overview = await Load();

        Assert.Equal(new YearMonth(2026, 10), overview.Latest);
        Assert.Equal(0, overview.LatestCents);
        Assert.Empty(overview.Slices);
        Assert.Equal(new Comparison(new YearMonth(2026, 9), -500, -100.0m), overview.Versus);
        Assert.Equal("-100,0%", SummaryService.FormatPercent(overview.Versus!.Percent!.Value));
    }

    [Fact]
    public async Task VersusIsAbsentWithOneMonth()
    {
        var rent = await db.Category("Rent");
        var food = await db.Category("Food");
        await db.Line(2026, 10, rent, 1000);
        await db.Line(2026, 10, food, 250);

        var overview = await Load();

        Assert.Null(overview.Versus);
        Assert.Equal(overview.Latest, overview.First);
        Assert.Equal([new YearMonth(2026, 10)], overview.Months.Select(m => m.Month));
        Assert.Equal(1250, overview.AverageCents);
    }

    [Fact]
    public async Task VersusComparesWithTheMonthBefore()
    {
        var rent = await db.Category("Rent");
        await db.Line(2026, 9, rent, 202_000);
        await db.Line(2026, 10, rent, 210_900);

        var versus = (await Load()).Versus;

        Assert.Equal(new Comparison(new YearMonth(2026, 9), 8_900, 4.4m), versus);
        Assert.Equal("+ € 89,00", Money.FormatChange(versus!.ChangeCents));
        Assert.Equal(160, Money.FormatChange(versus.ChangeCents)[1]);
        Assert.Equal("+4,4%", SummaryService.FormatPercent(versus.Percent!.Value));
    }

    [Fact]
    public async Task PercentIsAbsentWhenTheMonthBeforeIsZero()
    {
        var rent = await db.Category("Rent");
        await db.Line(2026, 7, rent, 1000);
        await db.Line(2026, 9, rent, 0);
        await db.Line(2026, 10, rent, 1500);

        var versus = (await Load()).Versus;

        Assert.Equal(new Comparison(new YearMonth(2026, 9), 1500, null), versus);
    }

    [Fact]
    public async Task AverageCountsOnlyMonthsFromTheFirst()
    {
        var rent = await db.Category("Rent");
        await db.Line(2026, 8, rent, 100);
        await db.Line(2026, 10, rent, 201);

        var overview = await Load(Period.AllTime);

        // (100 + 0 + 201) / 3 = 100.33 → 100
        Assert.Equal(100, overview.AverageCents);
    }

    [Fact]
    public async Task AverageUsesTheTwelveMonthWindowOnly()
    {
        var rent = await db.Category("Rent");
        await db.Line(2024, 1, rent, 1_000_000);
        for (var month = 1; month <= 12; month++)
        {
            await db.Line(2026, month, rent, month * 100);
        }

        var all = await Load(Period.AllTime);

        Assert.Equal(650, all.AverageCents);
    }

    [Fact]
    public async Task CategoriesOrderByWindowTotalThenName()
    {
        var zoo = await db.Category("Zoo");
        var bikes = await db.Category("Bikes");
        var apples = await db.Category("apples");
        var rent = await db.Category("Rent");
        var old = await db.Category("Old");
        var gifts = await db.Category("Gifts");
        var unused = await db.Category("Unused");
        await db.Line(2026, 10, rent, 900);
        await db.Line(2026, 9, bikes, 300);
        await db.Line(2026, 10, apples, 300);
        await db.Line(2026, 3, zoo, 400);
        await db.Line(2026, 4, gifts, 50);
        await db.Line(2024, 1, old, 99_999);

        var overview = await Load();

        Assert.Equal(
            ["Rent", "Zoo", "apples", "Bikes", "Gifts", "Old", "Unused"],
            overview.Categories.Select(c => c.Name));
        Assert.Equal([rent, zoo, apples, bikes, gifts, old, unused], overview.Categories.Select(c => c.Id));
        Assert.Equal([900L, 400L, 300L, 300L, 50L, 0L, 0L], overview.Categories.Select(c => c.TotalCents));
        Assert.Equal(CategoryColors.At(1), overview.Categories[3].Color);
    }

    [Fact]
    public void PercentAndShareFormatDutch()
    {
        Assert.Equal("+4,4%", SummaryService.FormatPercent(4.4m));
        Assert.Equal("-12,0%", SummaryService.FormatPercent(-12m));
        Assert.Equal("0,0%", SummaryService.FormatPercent(0m));
        Assert.Equal("45,3%", SummaryService.FormatShare(0.4527m));
        Assert.Equal("100,0%", SummaryService.FormatShare(1m));
    }
}
