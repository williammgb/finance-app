using System.Globalization;
using System.Text.RegularExpressions;

namespace FinanceApp.Services;

/// <summary>
/// Amounts as whole cents: reading what a person types, and showing cents as euros, Dutch style.
/// </summary>
public static partial class Money
{
    public const string ParseError = "Enter an amount like 12,50";

    private const long MaxCents = 100_000_000;
    private const char NoBreakSpace = ' ';

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    [GeneratedRegex(@"^([0-9]+)(?:[.,]([0-9]{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    /// <summary>
    /// Reads <c>12,50</c> or <c>12.50</c> into cents. Digits, at most one separator, at most two
    /// decimals, no sign, no thousands dots, 0 to 1,000,000 euros.
    /// </summary>
    public static bool TryParse(string? text, out long cents)
    {
        cents = 0;
        if (text is null)
        {
            return false;
        }

        var match = AmountPattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        if (!long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var euros)
            || euros > MaxCents / 100)
        {
            return false;
        }

        var fraction = match.Groups[2].Value;
        var fractionCents = fraction.Length switch
        {
            0 => 0,
            1 => (fraction[0] - '0') * 10,
            _ => (fraction[0] - '0') * 10 + (fraction[1] - '0'),
        };

        var total = euros * 100 + fractionCents;
        if (total > MaxCents)
        {
            return false;
        }

        cents = total;
        return true;
    }

    /// <summary><c>€ 1.234,56</c>; a negative amount gets a leading <c>-</c>: <c>-€ 12,00</c>.</summary>
    public static string Format(long cents)
    {
        var sign = cents < 0 ? "-" : "";
        return sign + "€" + NoBreakSpace + Math.Abs(cents / 100m).ToString("N2", Dutch);
    }

    /// <summary><c>+ € 89,00</c>, <c>- € 12,00</c>, <c>€ 0,00</c>.</summary>
    public static string FormatChange(long cents)
    {
        var amount = "€" + NoBreakSpace + Math.Abs(cents / 100m).ToString("N2", Dutch);
        return cents switch
        {
            > 0 => "+" + NoBreakSpace + amount,
            < 0 => "-" + NoBreakSpace + amount,
            _ => amount,
        };
    }

    /// <summary><c>1234,56</c>: what an edit form puts in the amount field, read back by <see cref="TryParse"/>.</summary>
    public static string ToInput(long cents) => (cents / 100m).ToString("0.00", Dutch);
}
