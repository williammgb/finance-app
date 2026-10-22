using System.Globalization;
using FinanceApp.Storage;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Services;

/// <summary>A calendar month.</summary>
public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    /// <summary>Months since year 0: consecutive months have consecutive indexes.</summary>
    public int Index => Year * 12 + Month - 1;

    public YearMonth Previous => FromIndex(Index - 1);

    /// <summary><c>Nov 25</c>.</summary>
    public string Label => new DateTime(Year, Month, 1).ToString("MMM yy", CultureInfo.InvariantCulture);

    /// <summary><c>November 2025</c>.</summary>
    public string Title => new DateTime(Year, Month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

    public static YearMonth FromIndex(int index) => new(index / 12, index % 12 + 1);

    public YearMonth AddMonths(int months) => FromIndex(Index + months);

    public int CompareTo(YearMonth other) => Index.CompareTo(other.Index);
}

/// <summary>Which months the line chart shows.</summary>
public enum Period
{
    /// <summary>The 12 months up to the latest month with a line, never before the first.</summary>
    TwelveMonths,

    /// <summary>From the first month with a line to the latest.</summary>
    AllTime,
}

/// <summary>A category with its colour and its total over the shown months.</summary>
public sealed record SummaryCategory(int Id, string Name, string Color, long TotalCents);

/// <summary>One month of the line chart: its total and the amount per category Id (missing = 0).</summary>
public sealed record MonthTotals(YearMonth Month, long TotalCents, IReadOnlyDictionary<int, long> CentsByCategory)
{
    public long CentsOf(int categoryId) => CentsByCategory.GetValueOrDefault(categoryId);
}

/// <summary>One slice of the donut; <see cref="Share"/> is a fraction of the month total, 0 to 1.</summary>
public sealed record DonutSlice(string Name, string Color, long Cents, decimal Share);

/// <summary>The latest month against the calendar month before it; <see cref="Percent"/> is null when that month was 0.</summary>
public sealed record Comparison(YearMonth Month, long ChangeCents, decimal? Percent);

/// <summary>Everything the overview page shows.</summary>
/// <param name="Versus">Null when the latest month is the only month with data.</param>
/// <param name="AverageCents">The mean monthly total of the 12-month window, from the first month with a line.</param>
/// <param name="Months">The months of the asked period, oldest first, gaps as 0.</param>
/// <param name="Categories">Every category, by its total over <paramref name="Months"/>, largest first; ties by name.</param>
/// <param name="Slices">The latest month's categories above 0, largest first; ties by name.</param>
public sealed record Overview(
    YearMonth First,
    YearMonth Latest,
    long LatestCents,
    Comparison? Versus,
    long AverageCents,
    IReadOnlyList<MonthTotals> Months,
    IReadOnlyList<SummaryCategory> Categories,
    IReadOnlyList<DonutSlice> Slices);

/// <summary>The numbers of the overview page. Reads only; each call uses its own context.</summary>
public sealed class SummaryService(IDbContextFactory<FinanceDb> dbFactory)
{
    public const int WindowMonths = 12;

    /// <summary>How many categories the line chart shows before any are switched on.</summary>
    public const int ShownByDefault = 5;

    private static readonly CultureInfo Dutch = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>The overview for <paramref name="period"/>, or null when there are no lines at all.</summary>
    public async Task<Overview?> Load(Period period)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var sums = await db.Entries
            .GroupBy(e => new { e.Year, e.Month, e.CategoryId })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.CategoryId, Cents = g.Sum(e => e.AmountCents) })
            .ToListAsync();
        if (sums.Count == 0)
        {
            return null;
        }

        var names = await db.Categories.ToDictionaryAsync(c => c.Id, c => c.Name);
        var colors = await CategoryService.ColorsById(db);

        var byMonth = sums
            .GroupBy(s => new YearMonth(s.Year, s.Month))
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<int, long>)g.ToDictionary(s => s.CategoryId, s => s.Cents));
        var first = byMonth.Keys.Min();
        var latest = byMonth.Keys.Max();

        var twelveStart = latest.AddMonths(-(WindowMonths - 1));
        var twelve = Months(twelveStart.CompareTo(first) > 0 ? twelveStart : first, latest, byMonth);
        var months = period == Period.AllTime ? Months(first, latest, byMonth) : twelve;

        var categories = names
            .Select(c => new SummaryCategory(c.Key, c.Value, colors[c.Key], months.Sum(m => m.CentsOf(c.Key))))
            .OrderByDescending(c => c.TotalCents)
            .ThenBy(c => c.Name, CategoryService.NameOrder)
            .ThenBy(c => c.Id)
            .ToList();

        var latestCents = byMonth[latest].Values.Sum();
        Comparison? versus = null;
        if (first != latest)
        {
            var before = latest.Previous;
            var beforeCents = byMonth.TryGetValue(before, out var b) ? b.Values.Sum() : 0;
            var change = latestCents - beforeCents;
            decimal? percent = beforeCents == 0 ? null : Math.Round(change * 100m / beforeCents, 1, MidpointRounding.AwayFromZero);
            versus = new Comparison(before, change, percent);
        }

        var average = (long)Math.Round((decimal)twelve.Sum(m => m.TotalCents) / twelve.Count, MidpointRounding.AwayFromZero);

        var slices = byMonth[latest]
            .Where(p => p.Value > 0)
            .Select(p => new DonutSlice(names[p.Key], colors[p.Key], p.Value, (decimal)p.Value / latestCents))
            .OrderByDescending(s => s.Cents)
            .ThenBy(s => s.Name, CategoryService.NameOrder)
            .ToList();

        return new Overview(first, latest, latestCents, versus, average, months, categories, slices);
    }

    /// <summary><c>+4,4%</c>, <c>-12,0%</c>, <c>0,0%</c>: a change in percent, one decimal.</summary>
    public static string FormatPercent(decimal percent)
    {
        var sign = percent switch
        {
            > 0 => "+",
            < 0 => "-",
            _ => "",
        };
        return sign + Math.Abs(percent).ToString("0.0", Dutch) + "%";
    }

    /// <summary><c>45,3%</c>: a share between 0 and 1 in percent, one decimal.</summary>
    public static string FormatShare(decimal share) =>
        Math.Round(share * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", Dutch) + "%";

    private static List<MonthTotals> Months(YearMonth from, YearMonth to, Dictionary<YearMonth, IReadOnlyDictionary<int, long>> byMonth)
    {
        var empty = new Dictionary<int, long>();
        var months = new List<MonthTotals>();
        for (var index = from.Index; index <= to.Index; index++)
        {
            var month = YearMonth.FromIndex(index);
            var cents = byMonth.TryGetValue(month, out var found) ? found : empty;
            months.Add(new MonthTotals(month, cents.Values.Sum(), cents));
        }

        return months;
    }
}
