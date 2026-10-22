using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FinanceApp.Storage;

namespace FinanceApp.Services;

/// <summary>One spending line in a CSV file; <see cref="Category"/> is the category's name.</summary>
public sealed record CsvLine(int Year, int Month, string Category, long AmountCents, string? Note);

/// <summary>What a CSV file holds: category names in order of first appearance, and the lines in file order.</summary>
public sealed record CsvData(IReadOnlyList<string> Categories, IReadOnlyList<CsvLine> Lines);

/// <summary>A file read: its <see cref="Data"/>, or the <see cref="Errors"/> that refuse it.</summary>
public sealed record CsvRead(CsvData? Data, IReadOnlyList<string> Errors)
{
    [MemberNotNullWhen(true, nameof(Data))]
    public bool Ok => Data is not null;
}

/// <summary>
/// The one CSV format for export and import: UTF-8 with BOM, comma-separated, <c>\r\n</c> line ends
/// (<c>\n</c> read too), header <c>Year,Month,Category,Amount,Note</c>. Every category comes first as a
/// category-only row (<c>,,name,,</c>), then the lines. A field holding a comma, quote or line end is
/// quoted, a quote inside doubled.
/// </summary>
public static partial class CsvFormat
{
    public const string Header = "Year,Month,Category,Amount,Note";
    public const long MaxBytes = 5 * 1024 * 1024;

    public const string FileEmpty = "The file is empty";
    public const string FileTooLarge = "File is larger than 5 MB";
    public const string HeaderWrong = "First line must be " + Header;

    private const string NewLine = "\r\n";
    private const int FieldCount = 5;

    private static readonly string[] HeaderFields = Header.Split(',');
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [GeneratedRegex(@"^([0-9]+)(?:\.([0-9]{1,2}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex AmountPattern();

    /// <summary>Category rows in the given order, then the lines in the given order.</summary>
    public static string Write(IEnumerable<string> categories, IEnumerable<CsvLine> lines)
    {
        var text = new StringBuilder(Header).Append(NewLine);
        foreach (var name in categories)
        {
            text.Append(",,").Append(Quote(name)).Append(",,").Append(NewLine);
        }

        foreach (var line in lines)
        {
            text.Append(line.Year.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(line.Month.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Quote(line.Category)).Append(',')
                .Append(Amount(line.AmountCents)).Append(',')
                .Append(Quote(line.Note ?? "")).Append(NewLine);
        }

        return text.ToString();
    }

    /// <summary>The text as UTF-8 bytes with a BOM, so Excel shows <c>ë</c> right.</summary>
    public static byte[] Encode(string text) => [.. Bom, .. Utf8.GetBytes(text)];

    /// <summary>UTF-8 bytes as text; a BOM is dropped, and a file without one reads the same.</summary>
    public static string Decode(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith(Bom) ? Bom.Length : 0;
        return Utf8.GetString(bytes, start, bytes.Length - start);
    }

    /// <summary><see cref="FileTooLarge"/> for a file over 5 MB, checked before it is read.</summary>
    public static string? CheckSize(long bytes) => bytes > MaxBytes ? FileTooLarge : null;

    /// <summary>
    /// The file's categories and lines, or every problem in it: one per bad row, by the file line the row
    /// starts on. Category names match ignoring case; the first spelling wins. Fields are trimmed; an empty
    /// note is <c>null</c>.
    /// </summary>
    public static CsvRead Read(string text)
    {
        if (text.StartsWith('﻿'))
        {
            text = text[1..];
        }

        if (text.Length == 0)
        {
            return Refuse(FileEmpty);
        }

        using var records = Records(text).GetEnumerator();
        records.MoveNext();
        var header = records.Current;
        if (header.Unclosed || !header.Fields.SequenceEqual(HeaderFields))
        {
            return Refuse(HeaderWrong);
        }

        var errors = new List<string>();
        var categories = new List<string>();
        var byKey = new Dictionary<string, string>();
        var lines = new List<CsvLine>();

        string Name(string candidate)
        {
            var key = Category.KeyOf(candidate);
            if (byKey.TryGetValue(key, out var known))
            {
                return known;
            }

            byKey[key] = candidate;
            categories.Add(candidate);
            return candidate;
        }

        while (records.MoveNext())
        {
            var record = records.Current;
            if (record.Unclosed)
            {
                errors.Add(QuoteNotClosed(record.Line));
                break;
            }

            if (!record.Blank && ReadRow(record.Line, record.Fields, Name, lines) is { } error)
            {
                errors.Add(error);
            }
        }

        return errors.Count > 0 ? new CsvRead(null, errors) : new CsvRead(new CsvData(categories, lines), []);
    }

    private static string YearWrong(int line) => $"Line {line}: year must be 2000–2100";

    private static string MonthWrong(int line) => $"Line {line}: month must be 1–12";

    private static string AmountWrong(int line) => $"Line {line}: amount must look like 12.50";

    private static string CategoryEmpty(int line) => $"Line {line}: category can't be empty";

    private static string CategoryTooLong(int line) => $"Line {line}: category name is too long";

    private static string NoteTooLong(int line) => $"Line {line}: note is too long";

    private static string FieldCountWrong(int line) => $"Line {line}: wrong number of fields";

    private static string QuoteNotClosed(int line) => $"Line {line}: quote is never closed";

    private static CsvRead Refuse(string error) => new(null, [error]);

    /// <summary>A category row (Year, Month and Amount empty) or a line; anything else is the row's error.</summary>
    private static string? ReadRow(int at, List<string> fields, Func<string, string> name, List<CsvLine> lines)
    {
        if (fields.Count != FieldCount)
        {
            return FieldCountWrong(at);
        }

        var year = fields[0].Trim();
        var month = fields[1].Trim();
        var category = fields[2].Trim();
        var amount = fields[3].Trim();
        var note = fields[4].Trim();

        if (category.Length == 0)
        {
            return CategoryEmpty(at);
        }

        if (category.Length > Category.MaxNameLength)
        {
            return CategoryTooLong(at);
        }

        if (year.Length == 0 && month.Length == 0 && amount.Length == 0)
        {
            name(category);
            return null;
        }

        if (!TryNumber(year, out var y) || y is < EntryService.MinYear or > EntryService.MaxYear)
        {
            return YearWrong(at);
        }

        if (!TryNumber(month, out var m) || m is < 1 or > 12)
        {
            return MonthWrong(at);
        }

        if (!TryAmount(amount, out var cents))
        {
            return AmountWrong(at);
        }

        if (note.Length > Entry.MaxNoteLength)
        {
            return NoteTooLong(at);
        }

        lines.Add(new CsvLine(y, m, name(category), cents, note.Length == 0 ? null : note));
        return null;
    }

    private static bool TryNumber(string text, out int number) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    /// <summary><c>12</c>, <c>12.5</c> or <c>12.50</c>: a dot, at most two decimals, 0 to 1,000,000 euros.</summary>
    private static bool TryAmount(string text, out long cents)
    {
        cents = 0;
        var match = AmountPattern().Match(text);
        if (!match.Success
            || !long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var euros)
            || euros > EntryService.MaxCents / 100)
        {
            return false;
        }

        var fraction = match.Groups[2].Value.PadRight(2, '0');
        var total = euros * 100 + (fraction[0] - '0') * 10 + (fraction[1] - '0');
        if (total > EntryService.MaxCents)
        {
            return false;
        }

        cents = total;
        return true;
    }

    private static string Amount(long cents) =>
        string.Create(CultureInfo.InvariantCulture, $"{cents / 100}.{cents % 100:00}");

    private static string Quote(string field) =>
        field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;

    /// <summary>One row of the file: the line it starts on, its fields, and whether it is blank or ends inside quotes.</summary>
    private readonly record struct Record(int Line, List<string> Fields, bool Unclosed, bool Blank);

    /// <summary>
    /// The rows, split on <c>\r\n</c> or <c>\n</c> outside quotes. A quote opens a quoted field only at the
    /// start of a field; inside one, <c>""</c> is a quote and line ends are kept. A row still in quotes at
    /// the end of the text is the last one.
    /// </summary>
    private static IEnumerable<Record> Records(string text)
    {
        var at = 1;
        var i = 0;
        while (i < text.Length)
        {
            var start = at;
            var fields = new List<string>();
            var field = new StringBuilder();
            var anyQuoted = false;
            var inQuotes = false;
            var fieldStart = true;
            while (i < text.Length)
            {
                var c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                        }
                        else
                        {
                            inQuotes = false;
                            i++;
                        }

                        continue;
                    }

                    if (c == '\n')
                    {
                        at++;
                    }

                    field.Append(c);
                    i++;
                    continue;
                }

                if (c == '"' && fieldStart)
                {
                    inQuotes = true;
                    anyQuoted = true;
                    fieldStart = false;
                    i++;
                    continue;
                }

                if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                    fieldStart = true;
                    i++;
                    continue;
                }

                if (c == '\n' || (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n'))
                {
                    i += c == '\n' ? 1 : 2;
                    at++;
                    break;
                }

                field.Append(c);
                fieldStart = false;
                i++;
            }

            fields.Add(field.ToString());
            var blank = !anyQuoted && fields.Count == 1 && fields[0].Trim().Length == 0;
            yield return new Record(start, fields, inQuotes, blank);
            if (inQuotes)
            {
                yield break;
            }
        }
    }
}
