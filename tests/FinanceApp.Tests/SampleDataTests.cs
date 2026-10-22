using FinanceApp.Services;

namespace FinanceApp.Tests;

/// <summary>The committed demo data, samples/example-spending.csv, has the facts the demo promises.</summary>
public sealed class SampleDataTests
{
    private static readonly string[] DemoCategories =
    [
        "Groceries", "Eating out", "Rent", "Health", "Clothing", "Entertainment",
        "Subscriptions", "Gifts", "Sport", "Transport", "Utilities",
    ];

    private static int MonthIndex(int year, int month) => year * 12 + month - 1;

    private static CsvData ReadSample()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "FinanceApp.slnx")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        var path = Path.Combine(folder.FullName, "samples", "example-spending.csv");
        var read = CsvFormat.Read(CsvFormat.Decode(File.ReadAllBytes(path)));
        Assert.True(read.Ok, string.Join("; ", read.Errors));
        return read.Data;
    }

    [Fact]
    public void HasTheElevenCategoriesInOrder()
    {
        Assert.Equal(DemoCategories, ReadSample().Categories);
    }

    [Fact]
    public void HasLinesInExactlyTheTwentyFourMonthsFromNov2024ToOct2026()
    {
        var months = ReadSample().Lines.Select(l => MonthIndex(l.Year, l.Month)).Distinct().Order();

        Assert.Equal(Enumerable.Range(MonthIndex(2024, 11), 24), months);
    }

    [Fact]
    public void SportStartsInMarch2025()
    {
        var sport = ReadSample().Lines.Where(l => l.Category == "Sport").Select(l => MonthIndex(l.Year, l.Month)).ToList();

        Assert.Equal(MonthIndex(2025, 3), sport.Min());
    }

    [Fact]
    public void July2026HasTheHighestTotal()
    {
        var totals = ReadSample().Lines
            .GroupBy(l => MonthIndex(l.Year, l.Month))
            .ToDictionary(g => g.Key, g => g.Sum(l => l.AmountCents));
        var july = totals[MonthIndex(2026, 7)];

        Assert.All(totals.Where(t => t.Key != MonthIndex(2026, 7)), t => Assert.True(t.Value < july, $"month {t.Key} totals {t.Value}, July 2026 {july}"));
    }

    [Fact]
    public void HasOneToFourLinesPerCategoryPerMonthAndANoteWithAComma()
    {
        var data = ReadSample();
        var counts = data.Lines.GroupBy(l => (l.Year, l.Month, l.Category)).Select(g => g.Count()).ToList();

        Assert.All(counts, count => Assert.InRange(count, 1, 4));
        Assert.Contains(data.Lines, l => l.Note?.Contains(',') == true);
    }

    [Fact]
    public async Task ImportsIntoAnEmptyAppAsTheDataPageSummaryShows()
    {
        using var db = new TestDb();
        var transfer = new TransferService(db.Factory);
        var data = ReadSample();

        var result = await transfer.Import(data);

        Assert.True(result.Ok, result.Error);
        Assert.Equal($"{data.Lines.Count} lines · 11 categories · Nov 2024 – Oct 2026", (await transfer.Current()).AppText);
        Assert.Equal(DemoCategories, (await db.Categories.List()).OrderBy(c => c.Id).Select(c => c.Name));
    }
}
