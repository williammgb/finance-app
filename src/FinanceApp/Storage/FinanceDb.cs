using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Storage;

/// <summary>The SQLite database: categories and their spending lines.</summary>
public class FinanceDb(DbContextOptions<FinanceDb> options) : DbContext(options)
{
    public const string FileName = "finance.db";

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Entry> Entries => Set<Entry>();

    /// <summary>
    /// The connection string for <c>&lt;contentRoot&gt;/&lt;dataFolder&gt;/finance.db</c>, after creating the
    /// folder. <paramref name="dataFolder"/> may be absolute; <c>Path.Combine</c> then keeps it as it is.
    /// </summary>
    public static string PrepareFile(string contentRoot, string dataFolder)
    {
        var folder = Path.Combine(contentRoot, dataFolder);
        Directory.CreateDirectory(folder);
        return new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, FileName) }.ToString();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(category =>
        {
            category.Property(c => c.Name).HasMaxLength(Category.MaxNameLength).IsRequired();
            category.Property(c => c.NameKey).HasMaxLength(Category.MaxNameLength).IsRequired();
            category.HasIndex(c => c.NameKey).IsUnique();
        });

        modelBuilder.Entity<Entry>(entry =>
        {
            entry.Property(e => e.Note).HasMaxLength(global::FinanceApp.Storage.Entry.MaxNoteLength);
            entry.HasIndex(e => new { e.Year, e.Month });
            entry.HasOne(e => e.Category)
                .WithMany(c => c.Entries)
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
