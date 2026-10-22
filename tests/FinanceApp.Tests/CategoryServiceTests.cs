using FinanceApp.Services;
using FinanceApp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Tests;

public sealed class CategoryServiceTests : IDisposable
{
    private readonly TestDb db = new();

    public void Dispose() => db.Dispose();

    private async Task<string[]> Names() => (await db.Categories.List()).Select(c => c.Name).ToArray();

    [Fact]
    public async Task AddTrimsTheName()
    {
        var result = await db.Categories.Add("  Pets  ");

        Assert.True(result.Ok);
        Assert.Equal(["Pets"], await Names());
    }

    [Fact]
    public async Task AddRefusesSameNameInOtherCase()
    {
        await db.Category("Pets");

        var result = await db.Categories.Add("pets");

        Assert.Equal("A category with that name already exists", result.Error);
        Assert.Equal(["Pets"], await Names());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AddRefusesEmptyName(string? name)
    {
        var result = await db.Categories.Add(name);

        Assert.Equal("Name can't be empty", result.Error);
        Assert.Empty(await Names());
    }

    [Fact]
    public async Task AddRefusesTooLongNameButTakesForty()
    {
        var tooLong = await db.Categories.Add(new string('a', 41));
        var forty = await db.Categories.Add(" " + new string('b', 40) + " ");

        Assert.Equal("Name is too long", tooLong.Error);
        Assert.True(forty.Ok);
        Assert.Equal([new string('b', 40)], await Names());
    }

    [Fact]
    public async Task RenameMayChangeOnlyCase()
    {
        var id = await db.Category("Gezondheid");

        var result = await db.Categories.Rename(id, "gezondheid");

        Assert.True(result.Ok);
        Assert.Equal(["gezondheid"], await Names());
    }

    [Fact]
    public async Task RenameRefusesAnotherCategorysName()
    {
        await db.Category("Pets");
        var food = await db.Category("Food");

        var result = await db.Categories.Rename(food, "  PETS ");

        Assert.Equal("A category with that name already exists", result.Error);
        Assert.Equal(["Food", "Pets"], await Names());
    }

    [Theory]
    [InlineData("  ", "Name can't be empty")]
    [InlineData("12345678901234567890123456789012345678901", "Name is too long")]
    public async Task RenameChecksTheName(string name, string message)
    {
        var id = await db.Category("Food");

        var result = await db.Categories.Rename(id, name);

        Assert.Equal(message, result.Error);
        Assert.Equal(["Food"], await Names());
    }

    [Fact]
    public async Task RenameOrDeleteUnknownCategoryIsNotFound()
    {
        Assert.Equal("Category not found", (await db.Categories.Rename(99, "Food")).Error);
        Assert.Equal("Category not found", (await db.Categories.Delete(99)).Error);
    }

    [Fact]
    public async Task DeleteRefusesCategoryInUse()
    {
        var food = await db.Category("Food");
        await db.Line(2026, 10, food, 1250);

        var result = await db.Categories.Delete(food);

        Assert.Equal("Category is in use", result.Error);
        Assert.Equal(["Food"], await Names());
        Assert.Single(await db.Entries.ForMonth(2026, 10));
    }

    [Fact]
    public async Task DeleteRemovesUnusedCategory()
    {
        var food = await db.Category("Food");
        await db.Category("Pets");

        var result = await db.Categories.Delete(food);

        Assert.True(result.Ok);
        Assert.Equal(["Pets"], await Names());
    }

    [Fact]
    public async Task RenamedCategoryShowsOnItsLines()
    {
        var food = await db.Category("Food");
        await db.Line(2026, 10, food, 1250);

        await db.Categories.Rename(food, "Boodschappen");

        Assert.Equal("Boodschappen", Assert.Single(await db.Entries.ForMonth(2026, 10)).CategoryName);
    }

    [Fact]
    public async Task ListSortsByNameIgnoringCaseWithColoursByIdAndTotals()
    {
        var zoo = await db.Category("Zoo");
        var apples = await db.Category("apples");
        var bikes = await db.Category("Bikes");
        await db.Line(2026, 9, apples, 1250);
        await db.Line(2026, 10, apples, 0);
        await db.Line(2025, 1, apples, 750, "market");
        await db.Line(2026, 10, zoo, 100_000_000);

        var list = await db.Categories.List();

        Assert.Equal(
            [
                new CategorySummary(apples, "apples", CategoryColors.At(1), 3, 2000),
                new CategorySummary(bikes, "Bikes", CategoryColors.At(2), 0, 0),
                new CategorySummary(zoo, "Zoo", CategoryColors.At(0), 1, 100_000_000),
            ],
            list);
    }

    [Fact]
    public async Task ColoursKeepIdOrderAfterADelete()
    {
        var first = await db.Category("First");
        await db.Category("Second");
        await db.Category("Third");

        await db.Categories.Delete(first);

        var list = await db.Categories.List();
        Assert.Equal([CategoryColors.At(0), CategoryColors.At(1)], list.Select(c => c.Color));
    }

    [Theory]
    [InlineData("data")]
    [InlineData("nested/data")]
    public void MigrateCreatesMissingFolderAndDatabase(string dataFolder)
    {
        var root = Path.Combine(Path.GetTempPath(), "finance-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var connectionString = FinanceDb.PrepareFile(root, dataFolder);
            var options = new DbContextOptionsBuilder<FinanceDb>().UseSqlite(connectionString).Options;
            using (var finance = new FinanceDb(options))
            {
                finance.Database.Migrate();
                Assert.Contains(finance.Database.GetAppliedMigrations(), m => m.EndsWith("_Initial", StringComparison.Ordinal));
            }

            Assert.True(File.Exists(Path.Combine(root, dataFolder, "finance.db")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AbsoluteDataFolderIsUsedAsItIs()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "finance-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var connectionString = FinanceDb.PrepareFile("/somewhere/else", absolute);

            Assert.Equal(Path.Combine(absolute, "finance.db"), new SqliteConnectionStringBuilder(connectionString).DataSource);
            Assert.True(Directory.Exists(absolute));
        }
        finally
        {
            if (Directory.Exists(absolute))
            {
                Directory.Delete(absolute, recursive: true);
            }
        }
    }
}
