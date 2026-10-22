using FinanceApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Services;

/// <summary>A spending line as the pages show it, with its category's name and colour.</summary>
public sealed record EntryLine(
    int Id,
    int Year,
    int Month,
    int CategoryId,
    string CategoryName,
    string CategoryColor,
    long AmountCents,
    string? Note);

/// <summary>A month's spending lines: list, add, change, delete. Each call uses its own context.</summary>
public sealed class EntryService(IDbContextFactory<FinanceDb> dbFactory)
{
    public const string NoteTooLong = "Note is too long";
    public const string MonthOutOfRange = "Month must be 1–12";
    public const string YearOutOfRange = "Year must be 2000–2100";
    public const string NotFound = "Line not found";

    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const long MaxCents = 100_000_000;

    /// <summary>That month's lines, ordered by category name ignoring case, then line Id.</summary>
    public async Task<IReadOnlyList<EntryLine>> ForMonth(int year, int month)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var colors = await CategoryService.ColorsById(db);
        var rows = await db.Entries
            .Where(e => e.Year == year && e.Month == month)
            .Select(e => new { e.Id, e.CategoryId, CategoryName = e.Category.Name, e.AmountCents, e.Note })
            .ToListAsync();

        return rows
            .Select(e => new EntryLine(
                e.Id, year, month, e.CategoryId, e.CategoryName, colors[e.CategoryId], e.AmountCents, e.Note))
            .OrderBy(e => e.CategoryName, CategoryService.NameOrder)
            .ThenBy(e => e.Id)
            .ToList();
    }

    public async Task<Result> Add(int year, int month, int categoryId, long cents, string? note)
    {
        var noteError = CheckNote(note, out var clean);
        if ((CheckYear(year) ?? CheckMonth(month) ?? CheckAmount(cents) ?? noteError) is { } error)
        {
            return Result.Fail(error);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        if (!await db.Categories.AnyAsync(c => c.Id == categoryId))
        {
            return Result.Fail(CategoryService.NotFound);
        }

        var entry = new Entry { Year = year, Month = month, CategoryId = categoryId, AmountCents = cents, Note = clean };
        db.Entries.Add(entry);
        await db.SaveChangesAsync();
        return Result.Done(entry.Id);
    }

    /// <summary>Changes a line's category, amount and note; its month stays.</summary>
    public async Task<Result> Update(int id, int categoryId, long cents, string? note)
    {
        var noteError = CheckNote(note, out var clean);
        if ((CheckAmount(cents) ?? noteError) is { } error)
        {
            return Result.Fail(error);
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.Entries.FindAsync(id);
        if (entry is null)
        {
            return Result.Fail(NotFound);
        }

        if (!await db.Categories.AnyAsync(c => c.Id == categoryId))
        {
            return Result.Fail(CategoryService.NotFound);
        }

        entry.CategoryId = categoryId;
        entry.AmountCents = cents;
        entry.Note = clean;
        await db.SaveChangesAsync();
        return Result.Done(id);
    }

    public async Task<Result> Delete(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.Entries.FindAsync(id);
        if (entry is null)
        {
            return Result.Fail(NotFound);
        }

        db.Entries.Remove(entry);
        await db.SaveChangesAsync();
        return Result.Done(id);
    }

    private static string? CheckYear(int year) => year is < MinYear or > MaxYear ? YearOutOfRange : null;

    private static string? CheckMonth(int month) => month is < 1 or > 12 ? MonthOutOfRange : null;

    private static string? CheckAmount(long cents) => cents is < 0 or > MaxCents ? Money.ParseError : null;

    /// <summary>Trims the note; empty or only spaces becomes <c>null</c>.</summary>
    private static string? CheckNote(string? note, out string? clean)
    {
        var trimmed = note?.Trim();
        clean = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        return clean is { Length: > Entry.MaxNoteLength } ? NoteTooLong : null;
    }
}
