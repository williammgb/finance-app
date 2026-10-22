using System.Text;
using FinanceApp.Services;

namespace FinanceApp.Tests;

public sealed class CsvFormatTests
{
    private const string Head = "Year,Month,Category,Amount,Note\r\n";

    private static CsvData ReadOk(string text)
    {
        var read = CsvFormat.Read(text);
        Assert.True(read.Ok, string.Join("; ", read.Errors));
        return read.Data;
    }

    private static string[] Errors(string text)
    {
        var read = CsvFormat.Read(text);
        Assert.False(read.Ok);
        Assert.Null(read.Data);
        return [.. read.Errors];
    }

    [Fact]
    public void WriteQuotesCommaQuoteAndNewlineAndReadsThemBack()
    {
        CsvLine[] lines =
        [
            new(2025, 1, "Zorg", 1250, "tandarts, zorgverzekering, zooltjes"),
            new(2025, 1, "Zorg", 5, "zei \"hoi\""),
            new(2025, 2, "Zorg", 300, "een\r\ntwee\ndrie"),
            new(2025, 2, "Zorg", 0, null),
        ];

        var text = CsvFormat.Write(["Zorg", "Eten, drinken"], lines);

        Assert.Equal(
            Head
            + ",,Zorg,,\r\n"
            + ",,\"Eten, drinken\",,\r\n"
            + "2025,1,Zorg,12.50,\"tandarts, zorgverzekering, zooltjes\"\r\n"
            + "2025,1,Zorg,0.05,\"zei \"\"hoi\"\"\"\r\n"
            + "2025,2,Zorg,3.00,\"een\r\ntwee\ndrie\"\r\n"
            + "2025,2,Zorg,0.00,\r\n",
            text);
        var data = ReadOk(text);
        Assert.Equal(["Zorg", "Eten, drinken"], data.Categories.ToArray());
        Assert.Equal(lines, data.Lines.ToArray());
    }

    [Fact]
    public void NonAsciiSurvivesEncodeAndDecode()
    {
        CsvLine[] lines = [new(2026, 8, "Financiën", 88400, "café € 3, één")];

        var data = ReadOk(CsvFormat.Decode(CsvFormat.Encode(CsvFormat.Write(["Financiën"], lines))));

        Assert.Equal(["Financiën"], data.Categories.ToArray());
        Assert.Equal(lines, data.Lines.ToArray());
    }

    [Fact]
    public void EncodeWritesUtf8WithBom()
    {
        var text = Head + "2025,1,Eten,1.00,ë\r\n";

        var bytes = CsvFormat.Encode(text);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(Encoding.UTF8.GetBytes(text), bytes[3..]);
    }

    [Fact]
    public void ReadAcceptsAFileWithAndWithoutBom()
    {
        var text = Head + "2025,1,Eten,1.00,ë\r\n";
        var utf8 = Encoding.UTF8.GetBytes(text);

        var withBom = ReadOk(CsvFormat.Decode([0xEF, 0xBB, 0xBF, .. utf8]));
        var withoutBom = ReadOk(CsvFormat.Decode(utf8));
        var bomCharacter = ReadOk("﻿" + text);

        CsvLine[] expected = [new(2025, 1, "Eten", 100, "ë")];
        Assert.Equal(expected, withBom.Lines.ToArray());
        Assert.Equal(expected, withoutBom.Lines.ToArray());
        Assert.Equal(expected, bomCharacter.Lines.ToArray());
    }

    [Fact]
    public void ReadAcceptsLfLineEnds()
    {
        var data = ReadOk("Year,Month,Category,Amount,Note\n,,Huur,,\n2025,12,Huur,884,juni\n2026,1,Huur,12.5,");

        Assert.Equal(["Huur"], data.Categories.ToArray());
        Assert.Equal([new(2025, 12, "Huur", 88400, "juni"), new CsvLine(2026, 1, "Huur", 1250, null)], data.Lines.ToArray());
    }

    [Fact]
    public void HeaderOnlyHoldsNothing()
    {
        foreach (var text in new[] { Head, "Year,Month,Category,Amount,Note", "﻿" + Head + "\r\n" })
        {
            var data = ReadOk(text);
            Assert.Empty(data.Categories);
            Assert.Empty(data.Lines);
        }
    }

    [Fact]
    public void CategoriesComeInOrderOfFirstAppearanceIgnoringCaseFirstSpellingWins()
    {
        var data = ReadOk(
            Head
            + ",,Eten,,\r\n"
            + "2025,1,ETEN,1.00,\r\n"
            + "\r\n"
            + "2025,1,Huur,2.00,\r\n"
            + ",,huur,,\r\n"
            + ",,Leeg,,\r\n");

        Assert.Equal(["Eten", "Huur", "Leeg"], data.Categories.ToArray());
        Assert.Equal(["Eten", "Huur"], data.Lines.Select(l => l.Category).ToArray());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("12", 1200)]
    [InlineData("12.5", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData(" 7.05 ", 705)]
    [InlineData("1000000.00", 100_000_000)]
    public void ReadAcceptsAmounts(string amount, long cents)
    {
        var data = ReadOk(Head + $"2025,1,A,{amount},\r\n");

        Assert.Equal(cents, Assert.Single(data.Lines).AmountCents);
    }

    [Theory]
    [InlineData("", "The file is empty")]
    [InlineData("﻿", "The file is empty")]
    [InlineData("Year,Month,Category,Amount\r\n", "First line must be Year,Month,Category,Amount,Note")]
    [InlineData("year,month,category,amount,note\r\n", "First line must be Year,Month,Category,Amount,Note")]
    [InlineData("2025,1,A,1.00,\r\n", "First line must be Year,Month,Category,Amount,Note")]
    [InlineData("\r\n", "First line must be Year,Month,Category,Amount,Note")]
    public void FileErrors(string text, string error) => Assert.Equal([error], Errors(text));

    [Fact]
    public void EmptyBytesAndOnlyABomAreEmpty()
    {
        Assert.Equal(["The file is empty"], Errors(CsvFormat.Decode([])));
        Assert.Equal(["The file is empty"], Errors(CsvFormat.Decode([0xEF, 0xBB, 0xBF])));
    }

    [Fact]
    public void FilesOverFiveMegabytesAreRefused()
    {
        Assert.Null(CsvFormat.CheckSize(5 * 1024 * 1024));
        Assert.Equal("File is larger than 5 MB", CsvFormat.CheckSize(5 * 1024 * 1024 + 1));
    }

    [Theory]
    [InlineData("1999,1,A,1.00,", "Line 2: year must be 2000–2100")]
    [InlineData("2101,1,A,1.00,", "Line 2: year must be 2000–2100")]
    [InlineData("abc,1,A,1.00,", "Line 2: year must be 2000–2100")]
    [InlineData("2025.0,1,A,1.00,", "Line 2: year must be 2000–2100")]
    [InlineData(",1,A,1.00,", "Line 2: year must be 2000–2100")]
    [InlineData("2025,0,A,1.00,", "Line 2: month must be 1–12")]
    [InlineData("2025,13,A,1.00,", "Line 2: month must be 1–12")]
    [InlineData("2025,Januari,A,1.00,", "Line 2: month must be 1–12")]
    [InlineData("2025,,A,1.00,", "Line 2: month must be 1–12")]
    [InlineData("2025,1,A,12.505,", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,A,-1.00,", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,A,\"12,50\",", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,A,1000000.01,", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,A,abc,", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,A,,", "Line 2: amount must look like 12.50")]
    [InlineData("2025,1,,1.00,", "Line 2: category can't be empty")]
    [InlineData("2025,1,   ,1.00,", "Line 2: category can't be empty")]
    [InlineData(",,,,", "Line 2: category can't be empty")]
    [InlineData("2025,1,A,1.00", "Line 2: wrong number of fields")]
    [InlineData("2025,1,A,1.00,note,extra", "Line 2: wrong number of fields")]
    [InlineData("2025,1,A,1.00,\"never closed", "Line 2: quote is never closed")]
    public void RowErrors(string row, string error) => Assert.Equal([error], Errors(Head + row + "\r\n"));

    [Fact]
    public void LongCategoryAndNoteAreRefusedPastTheLimit()
    {
        ReadOk(Head + $"2025,1,{new string('c', 40)},1.00,{new string('n', 200)}\r\n");

        Assert.Equal(["Line 2: category name is too long"], Errors(Head + $"2025,1,{new string('c', 41)},1.00,\r\n"));
        Assert.Equal(["Line 2: category name is too long"], Errors(Head + $",,{new string('c', 41)},,\r\n"));
        Assert.Equal(["Line 2: note is too long"], Errors(Head + $"2025,1,A,1.00,{new string('n', 201)}\r\n"));
    }

    [Fact]
    public void LineNumbersCountTheLinesInsideQuotes()
    {
        Assert.Equal(
            ["Line 4: month must be 1–12"],
            Errors(Head + "2025,1,A,1.00,\"one\ntwo\"\r\n2025,13,A,1.00,\r\n"));
        Assert.Equal(
            ["Line 3: quote is never closed"],
            Errors(Head + "2025,1,A,1.00,ok\r\n2025,1,A,1.00,\"never\nclosed\n"));
    }

    [Fact]
    public void EveryBadRowIsListed()
    {
        var errors = Errors(
            Head
            + "1999,1,A,1.00,\r\n"
            + "2025,1,A,1.00,fine\r\n"
            + "2025,13,A,1.00,\r\n"
            + "2025,1,A,x,\r\n");

        Assert.Equal(
            ["Line 2: year must be 2000–2100", "Line 4: month must be 1–12", "Line 5: amount must look like 12.50"],
            errors);
    }

    [Fact]
    public void FiveHundredRandomRowsReadBackEqual()
    {
        var random = new Random(20261022);
        const string alphabet = "abcXYZ 09,\"\n\r\tëé€&;";

        string Text(int maxLength)
        {
            var chars = new char[random.Next(0, maxLength + 1)];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[random.Next(alphabet.Length)];
            }

            // What the app holds: names and notes are stored trimmed.
            return new string(chars).Trim();
        }

        var categories = new List<string>();
        var keys = new HashSet<string>();
        while (categories.Count < 12)
        {
            var name = Text(40);
            if (name.Length > 0 && keys.Add(name.ToUpperInvariant()))
            {
                categories.Add(name);
            }
        }

        var lines = new List<CsvLine>();
        for (var i = 0; i < 500; i++)
        {
            var note = Text(random.Next(5) == 0 ? 200 : 30);
            var cents = random.Next(10) switch
            {
                0 => 0,
                1 => 100_000_000,
                _ => random.NextInt64(0, 100_000_001),
            };
            lines.Add(new CsvLine(
                random.Next(2000, 2101),
                random.Next(1, 13),
                categories[random.Next(categories.Count)],
                cents,
                note.Length == 0 ? null : note));
        }

        var data = ReadOk(CsvFormat.Decode(CsvFormat.Encode(CsvFormat.Write(categories, lines))));

        Assert.Equal(categories, data.Categories.ToList());
        Assert.Equal(lines, data.Lines.ToList());
        // The generator did produce the cases that need quoting.
        Assert.Contains(lines, l => l.Note?.Contains(',') == true);
        Assert.Contains(lines, l => l.Note?.Contains('"') == true);
        Assert.Contains(lines, l => l.Note?.Contains('\n') == true);
        Assert.Contains(lines, l => l.Note?.Contains("\r\n") == true);
        Assert.Contains(lines, l => l.Note is null);
        Assert.Contains(categories, c => c.Contains(',') || c.Contains('"'));
    }
}
