namespace FinanceApp.Storage;

/// <summary>One spending line: an amount in a category in a month, with an optional note.</summary>
public class Entry
{
    public const int MaxNoteLength = 200;

    public int Id { get; set; }

    /// <summary>2000 to 2100.</summary>
    public int Year { get; set; }

    /// <summary>1 to 12.</summary>
    public int Month { get; set; }

    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    /// <summary>Whole cents, 0 to 100,000,000.</summary>
    public long AmountCents { get; set; }

    /// <summary>At most 200 characters; <c>null</c> when empty.</summary>
    public string? Note { get; set; }
}
