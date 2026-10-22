using System.Text;
using FinanceApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Tests;

public sealed class TransferServiceTests : IDisposable
{
    private const string Head = "Year,Month,Category,Amount,Note\r\n";

    private readonly TestDb db = new();
    private readonly TransferService transfer;

    public TransferServiceTests() => transfer = new TransferService(db.Factory);

    public void Dispose() => db.Dispose();

    private static CsvData Parse(string text)
    {
        var read = CsvFormat.Read(text);
        Assert.True(read.Ok, string.Join("; ", read.Errors));
        return read.Data;
    }

    private static CsvData Parse(byte[] bytes) => Parse(CsvFormat.Decode(bytes));

    private async Task<string> ExportText() => CsvFormat.Decode(await transfer.Export());

    /// <summary>Four categories, one unused, and lines added out of month order.</summary>
    private async Task Seed()
    {
        var wonen = await db.Category("Wonen");
        var eten = await db.Category("Eten & drank");
        var kleding = await db.Category("Kleding, schoenen");
        await db.Category("Ongebruikt");
        await db.Line(2026, 8, eten, 4250, "zei \"lekker\"");
        await db.Line(2025, 1, kleding, 0);
        await db.Line(2025, 1, eten, 1250, "café, één\nnog een regel");
        await db.Line(2025, 1, wonen, 88400);
        await db.Line(2025, 1, eten, 300);
    }

    private const string SeedExport =
        Head
        + ",,Wonen,,\r\n"
        + ",,Eten & drank,,\r\n"
        + ",,\"Kleding, schoenen\",,\r\n"
        + ",,Ongebruikt,,\r\n"
        + "2025,1,Wonen,884.00,\r\n"
        + "2025,1,Eten & drank,12.50,\"café, één\nnog een regel\"\r\n"
        + "2025,1,Eten & drank,3.00,\r\n"
        + "2025,1,\"Kleding, schoenen\",0.00,\r\n"
        + "2026,8,Eten & drank,42.50,\"zei \"\"lekker\"\"\"\r\n";

    [Fact]
    public async Task ExportIsCategoriesInIdOrderThenLinesByMonthCategoryAndLine()
    {
        await Seed();

        var bytes = await transfer.Export();

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(SeedExport, CsvFormat.Decode(bytes));
    }

    [Fact]
    public async Task ExportReplaceExportGivesTheSameText()
    {
        await Seed();
        var first = await transfer.Export();

        var result = await transfer.Replace(Parse(first));

        Assert.True(result.Ok, result.Error);
        var second = await transfer.Export();
        Assert.Equal(Encoding.UTF8.GetString(first), Encoding.UTF8.GetString(second));
    }

    [Fact]
    public async Task CategoryIdsKeepTheirOrderThroughARoundTrip()
    {
        // Not alphabetical, so Id order differs from name order; colours follow Id order.
        string[] added = ["Zorg", "Auto", "Mode", "Boeken"];
        foreach (var name in added)
        {
            await db.Category(name);
        }

        var before = (await db.Categories.List()).Select(c => (c.Name, c.Color)).ToList();
        var export = await transfer.Export();

        Assert.True((await transfer.Replace(Parse(export))).Ok);
        Assert.Equal(before, (await db.Categories.List()).Select(c => (c.Name, c.Color)).ToList());
        await using (var context = db.Factory.CreateDbContext())
        {
            Assert.Equal(added, await context.Categories.OrderBy(c => c.Id).Select(c => c.Name).ToArrayAsync());
        }

        using var other = new TestDb();
        Assert.True((await new TransferService(other.Factory).Import(Parse(export))).Ok);
        Assert.Equal(before, (await other.Categories.List()).Select(c => (c.Name, c.Color)).ToList());
    }

    [Fact]
    public async Task ReplaceThatFailsOnInsertKeepsTheOldDataAndThrows()
    {
        await Seed();
        await using (var context = db.Factory.CreateDbContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER refuse_boom BEFORE INSERT ON Entries WHEN NEW.Note = 'boom' "
                + "BEGIN SELECT RAISE(ABORT, 'boom'); END");
        }

        var file = Parse(Head + "2025,1,Nieuw,1.00,fine\r\n2025,2,Nieuw,2.00,boom\r\n");

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => transfer.Replace(file));
        Assert.Equal(SeedExport, await ExportText());
    }

    [Fact]
    public async Task ImportFillsAnEmptyApp()
    {
        var result = await transfer.Import(Parse(
            Head + ",,Wonen,,\r\n2025,3,Eten,12.50,\r\n2025,1,wonen,500.00,\"  huur, januari  \"\r\n"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(
            Head + ",,Wonen,,\r\n,,Eten,,\r\n2025,1,Wonen,500.00,\"huur, januari\"\r\n2025,3,Eten,12.50,\r\n",
            await ExportText());
        var categories = await db.Categories.List();
        Assert.Equal(
            [("Eten", 1, 1250L), ("Wonen", 1, 50000L)],
            categories.Select(c => (c.Name, c.LineCount, c.TotalCents)).ToArray());
    }

    [Fact]
    public async Task ImportRefusesAnAppThatHoldsData()
    {
        await db.Category("Wonen");

        var result = await transfer.Import(Parse(Head + "2025,1,Eten,1.00,\r\n"));

        Assert.Equal("The app already holds data", result.Error);
        Assert.Equal(Head + ",,Wonen,,\r\n", await ExportText());
    }

    [Fact]
    public async Task HeaderOnlyImportsNothingAndReplaceEmptiesTheApp()
    {
        Assert.True((await transfer.Import(Parse(Head))).Ok);
        Assert.Equal("No data", (await transfer.Current()).AppText);

        await Seed();
        Assert.True((await transfer.Replace(Parse(Head))).Ok);
        Assert.Equal("No data", (await transfer.Current()).AppText);
        Assert.Equal(Head, await ExportText());
    }

    [Fact]
    public async Task SummaryTexts()
    {
        Assert.Equal("No data", (await transfer.Current()).AppText);
        await db.Category("Wonen");
        Assert.Equal("0 lines · 1 category", (await transfer.Current()).AppText);

        var file = Parse(
            Head + ",,wonen,,\r\n,,Eten,,\r\n2026,8,Eten,1.00,\r\n2025,1,Wonen,2.00,\r\n2025,11,Eten,3.00,\r\n");
        var summary = await transfer.Summarize(file);
        Assert.Equal("3 lines · 2 categories · Jan 2025 – Aug 2026", summary.Text);
        Assert.Equal(["Eten"], summary.NewCategories.ToArray());

        Assert.Equal("1 line · 1 category · Jan 2025", (await transfer.Summarize(Parse(Head + "2025,1,A,1.00,\r\n"))).Text);
        Assert.Equal("0 lines · 3 categories", (await transfer.Summarize(Parse(Head + ",,A,,\r\n,,B,,\r\n,,C,,\r\n"))).Text);
        Assert.Equal("0 lines · 0 categories", (await transfer.Summarize(Parse(Head))).Text);

        await Seed2();
        Assert.Equal("2 lines · 1 category · Dec 2024 – Jan 2025", (await transfer.Current()).AppText);
    }

    private async Task Seed2()
    {
        var wonen = (await db.Categories.List()).Single().Id;
        await db.Line(2025, 1, wonen, 100);
        await db.Line(2024, 12, wonen, 100);
    }
}
