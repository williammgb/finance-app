namespace FinanceApp.Services;

/// <summary>
/// Each category's fixed colour, by its 0-based position when categories are sorted by Id.
/// </summary>
public static class CategoryColors
{
    /// <summary>The ink colour of the Total line.</summary>
    public const string Total = "#1c1c1a";

    private static readonly string[] Palette =
    [
        "#2a78d6", "#eb6834", "#1baf7a", "#eda100", "#e87ba4", "#008300",
        "#6250d6", "#e34948", "#0097a7", "#b5651d", "#a8329a", "#8a9a00",
    ];

    /// <summary>The colour for a 0-based position; past 12 the colours repeat.</summary>
    public static string At(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        return Palette[position % Palette.Length];
    }
}
