using FinanceApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Services;

/// <summary>What a service call did: <see cref="Ok"/>, or the <see cref="Error"/> message a page shows.</summary>
/// <param name="Error">The message, or <c>null</c> when it worked.</param>
/// <param name="Id">The new row's Id after an add that worked; otherwise 0.</param>
public readonly record struct Result(string? Error, int Id = 0)
{
    public bool Ok => Error is null;

    public static Result Done(int id = 0) => new(null, id);

    public static Result Fail(string error) => new(error);
}

/// <summary>A category as the pages list it: its colour, how many lines use it and their total.</summary>
public sealed record CategorySummary(int Id, string Name, string Color, int LineCount, long TotalCents);

/// <summary>Lists, adds, renames and deletes categories. Each call uses its own context.</summary>
public sealed class CategoryService(IDbContextFactory<FinanceDb> dbFactory)
{
    public const string NameEmpty = "Name can't be empty";
    public const string NameTooLong = "Name is too long";
    public const string NameTaken = "A category with that name already exists";
    public const string InUse = "Category is in use";
    public const string NotFound = "Category not found";

    /// <summary>How category names are sorted wherever they are shown: ignoring case.</summary>
    public static readonly StringComparer NameOrder = StringComparer.InvariantCultureIgnoreCase;

    /// <summary>Every category, sorted by name ignoring case, with all-time line count and total.</summary>
    public async Task<IReadOnlyList<CategorySummary>> List()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var rows = await db.Categories
            .OrderBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.Name,
                LineCount = c.Entries.Count,
                TotalCents = c.Entries.Sum(e => (long?)e.AmountCents) ?? 0,
            })
            .ToListAsync();

        return rows
            .Select((c, position) => new CategorySummary(c.Id, c.Name, CategoryColors.At(position), c.LineCount, c.TotalCents))
            .OrderBy(c => c.Name, NameOrder)
            .ThenBy(c => c.Id)
            .ToList();
    }

    public async Task<Result> Add(string? name)
    {
        if (CheckName(name, out var clean) is { } error)
        {
            return Result.Fail(error);
        }

        var key = Category.KeyOf(clean);
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Categories.AnyAsync(c => c.NameKey == key))
        {
            return Result.Fail(NameTaken);
        }

        var category = new Category { Name = clean, NameKey = key };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return Result.Done(category.Id);
    }

    /// <summary>Renames; changing only the case of its own name is allowed.</summary>
    public async Task<Result> Rename(int id, string? name)
    {
        if (CheckName(name, out var clean) is { } error)
        {
            return Result.Fail(error);
        }

        var key = Category.KeyOf(clean);
        await using var db = await dbFactory.CreateDbContextAsync();
        var category = await db.Categories.FindAsync(id);
        if (category is null)
        {
            return Result.Fail(NotFound);
        }

        if (await db.Categories.AnyAsync(c => c.NameKey == key && c.Id != id))
        {
            return Result.Fail(NameTaken);
        }

        category.Name = clean;
        category.NameKey = key;
        await db.SaveChangesAsync();
        return Result.Done(id);
    }

    /// <summary>Deletes a category no line uses.</summary>
    public async Task<Result> Delete(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var category = await db.Categories.FindAsync(id);
        if (category is null)
        {
            return Result.Fail(NotFound);
        }

        if (await db.Entries.AnyAsync(e => e.CategoryId == id))
        {
            return Result.Fail(InUse);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return Result.Done(id);
    }

    /// <summary>Each category's colour by Id: <see cref="CategoryColors.At"/> of its position in Id order.</summary>
    internal static async Task<Dictionary<int, string>> ColorsById(FinanceDb db)
    {
        var ids = await db.Categories.OrderBy(c => c.Id).Select(c => c.Id).ToListAsync();
        return ids.Select((id, position) => (id, position)).ToDictionary(p => p.id, p => CategoryColors.At(p.position));
    }

    private static string? CheckName(string? name, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0)
        {
            return NameEmpty;
        }

        return clean.Length > Category.MaxNameLength ? NameTooLong : null;
    }
}
