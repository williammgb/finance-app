namespace FinanceApp.Storage;

/// <summary>A spending category. Names are unique ignoring case, through <see cref="NameKey"/>.</summary>
public class Category
{
    public const int MaxNameLength = 40;

    public int Id { get; set; }

    /// <summary>The name as typed, trimmed: 1 to 40 characters.</summary>
    public string Name { get; set; } = "";

    /// <summary><see cref="Name"/> upper-cased with the invariant culture; unique.</summary>
    public string NameKey { get; set; } = "";

    public List<Entry> Entries { get; set; } = [];

    public static string KeyOf(string name) => name.ToUpperInvariant();
}
