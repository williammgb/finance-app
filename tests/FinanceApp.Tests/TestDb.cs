using FinanceApp.Services;
using FinanceApp.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Tests;

/// <summary>
/// A fresh SQLite database in memory, kept alive by one open connection, with the services over it.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection connection;

    public TestDb()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FinanceDb>().UseSqlite(connection).Options;
        Factory = new OptionsFactory(options);
        using var db = Factory.CreateDbContext();
        db.Database.EnsureCreated();
        Categories = new CategoryService(Factory);
        Entries = new EntryService(Factory);
    }

    public IDbContextFactory<FinanceDb> Factory { get; }

    public CategoryService Categories { get; }

    public EntryService Entries { get; }

    /// <summary>Adds a category that must be accepted and returns its Id.</summary>
    public async Task<int> Category(string name)
    {
        var result = await Categories.Add(name);
        Assert.True(result.Ok, result.Error);
        return result.Id;
    }

    /// <summary>Adds a line that must be accepted and returns its Id.</summary>
    public async Task<int> Line(int year, int month, int categoryId, long cents, string? note = null)
    {
        var result = await Entries.Add(year, month, categoryId, cents, note);
        Assert.True(result.Ok, result.Error);
        return result.Id;
    }

    public void Dispose() => connection.Dispose();

    private sealed class OptionsFactory(DbContextOptions<FinanceDb> options) : IDbContextFactory<FinanceDb>
    {
        public FinanceDb CreateDbContext() => new(options);
    }
}
