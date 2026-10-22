using System.Globalization;
using FinanceApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Services;

/// <summary>
/// How much data there is: lines, categories, the first and last month with a line (as
/// <c>year * 12 + month - 1</c>), and — for a file — the categories the app does not have yet.
/// </summary>
public sealed record DataSummary(
    int Lines,
    int Categories,
    int? FirstMonth,
    int? LastMonth,
    IReadOnlyList<string> NewCategories)
{
    public bool IsEmpty => Lines == 0 && Categories == 0;

    /// <summary>
    /// <c>168 lines · 11 categories · Jan 2025 – Aug 2026</c>; one month: <c>… · Jan 2025</c>; no lines:
    /// <c>0 lines · 3 categories</c>.
    /// </summary>
    public string Text
    {
        get
        {
            var counts = $"{Count(Lines, "line", "lines")} · {Count(Categories, "category", "categories")}";
            if (FirstMonth is not { } first || LastMonth is not { } last)
            {
                return counts;
            }

            return first == last
                ? $"{counts} · {MonthName(first)}"
                : $"{counts} · {MonthName(first)} – {MonthName(last)}";
        }
    }

    /// <summary>The app's own summary: <see cref="Text"/>, or <c>No data</c> when it holds nothing.</summary>
    public string AppText => IsEmpty ? "No data" : Text;

    /// <summary><c>1 line</c>, <c>3 lines</c>.</summary>
    public static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    private static string MonthName(int index) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{CultureInfo.InvariantCulture.DateTimeFormat.GetAbbreviatedMonthName(index % 12 + 1)} {index / 12}");
}

/// <summary>
/// All data as one CSV file, and a CSV file in: into an empty app, or replacing everything, in one
/// transaction either way. Each call uses its own context.
/// </summary>
public sealed class TransferService(IDbContextFactory<FinanceDb> dbFactory)
{
    public const string NotEmpty = "The app already holds data";

    /// <summary>
    /// Every category in Id order, then every line by year, month, category Id and line Id; UTF-8 with BOM.
    /// Importing it gives each category the same place in Id order, so the same colour.
    /// </summary>
    public async Task<byte[]> Export()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var categories = await db.Categories.OrderBy(c => c.Id).Select(c => new { c.Id, c.Name }).ToListAsync();
        var names = categories.ToDictionary(c => c.Id, c => c.Name);
        var lines = await db.Entries
            .OrderBy(e => e.Year).ThenBy(e => e.Month).ThenBy(e => e.CategoryId).ThenBy(e => e.Id)
            .Select(e => new { e.Year, e.Month, e.CategoryId, e.AmountCents, e.Note })
            .ToListAsync();

        return CsvFormat.Encode(CsvFormat.Write(
            categories.Select(c => c.Name),
            lines.Select(e => new CsvLine(e.Year, e.Month, names[e.CategoryId], e.AmountCents, e.Note))));
    }

    /// <summary>What the app holds now.</summary>
    public async Task<DataSummary> Current()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var categories = await db.Categories.CountAsync();
        var lines = await db.Entries.CountAsync();
        var first = await db.Entries.MinAsync(e => (int?)(e.Year * 12 + e.Month - 1));
        var last = await db.Entries.MaxAsync(e => (int?)(e.Year * 12 + e.Month - 1));
        return new DataSummary(lines, categories, first, last, []);
    }

    /// <summary>What a read file holds, with the category names the app does not have yet (ignoring case).</summary>
    public async Task<DataSummary> Summarize(CsvData data)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var known = (await db.Categories.Select(c => c.NameKey).ToListAsync()).ToHashSet();
        var months = data.Lines.Select(l => l.Year * 12 + l.Month - 1).ToList();
        return new DataSummary(
            data.Lines.Count,
            data.Categories.Count,
            months.Count == 0 ? null : months.Min(),
            months.Count == 0 ? null : months.Max(),
            data.Categories.Where(name => !known.Contains(Category.KeyOf(name))).ToList());
    }

    /// <summary>Fills an app with no lines and no categories; otherwise <see cref="NotEmpty"/> and nothing changes.</summary>
    public async Task<Result> Import(CsvData data)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Categories.AnyAsync() || await db.Entries.AnyAsync())
        {
            return Result.Fail(NotEmpty);
        }

        await Insert(db, data);
        await transaction.CommitAsync();
        return Result.Done();
    }

    /// <summary>
    /// Deletes every line and category and imports the file, in one transaction: when anything fails the
    /// data is as before and the exception is thrown.
    /// </summary>
    public async Task<Result> Replace(CsvData data)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Entries.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await Insert(db, data);
        await transaction.CommitAsync();
        return Result.Done();
    }

    /// <summary>Categories one at a time, so their Ids follow the file's order; then the lines in file order.</summary>
    private static async Task Insert(FinanceDb db, CsvData data)
    {
        var ids = new Dictionary<string, int>();
        foreach (var name in data.Categories)
        {
            var category = new Category { Name = name, NameKey = Category.KeyOf(name) };
            db.Categories.Add(category);
            await db.SaveChangesAsync();
            ids[category.NameKey] = category.Id;
        }

        foreach (var line in data.Lines)
        {
            db.Entries.Add(new Entry
            {
                Year = line.Year,
                Month = line.Month,
                CategoryId = ids[Category.KeyOf(line.Category)],
                AmountCents = line.AmountCents,
                Note = line.Note,
            });
        }

        await db.SaveChangesAsync();
    }
}
