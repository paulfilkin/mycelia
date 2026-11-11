using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mycelia.Model;

/// <summary>How sure a date is: plain, about, before, after or calculated.</summary>
public enum DateModifier { None, About, Before, After, Calculated }

/// <summary>Display order for dates (see Design considerations, Dates).</summary>
public enum DateStyle
{
    /// <summary>05 Mar 1890</summary>
    DMY,
    /// <summary>5 Mar 1890</summary>
    DayMonthYear,
    /// <summary>05 March 1890</summary>
    LongDMY,
    /// <summary>Mar 5, 1890</summary>
    MDY,
    /// <summary>March 5, 1890</summary>
    LongMDY,
}

/// <summary>One calendar point; any part may be missing.</summary>
public readonly record struct DatePart(int? Year, int? Month, int? Day, bool DoubleDate = false)
{
    public bool HasYear => Year.HasValue;

    /// <summary>Day number for comparisons; missing month or day count as the first.</summary>
    public long? DayNumber => Year is int y ? GenDate.ToDayNumber(y, Month ?? 1, Day ?? 1) : null;
}

/// <summary>
/// A genealogical date as typed by the person: kept as text, read into parts when it can be.
/// Formats follow Design considerations, Dates.
/// </summary>
public sealed class GenDate
{
    private static readonly string[] MonthAbbrev =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private static readonly string[] MonthLong =
        ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    private static readonly string[] GedcomMonths =
        ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly int[] MonthDays = [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    private static readonly Regex RangeRegex = new(
        @"^(?:bet\.?|btw\.?|between)\s+(.+?)\s+(?:and|&|-|–)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FromToRegex = new(
        @"^from\s+(.+?)\s+to\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex YearRangeRegex = new(
        @"^(\d{3,4})\s*[-–]\s*(\d{3,4})$", RegexOptions.Compiled);

    private static readonly Regex ModifierRegex = new(
        @"^(about|abt|circa|ca|c|est|estimated|approx|approximately|bef|before|aft|after|cal|calc|calculated)(?:\.\s*|\s+)(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DoubleYearRegex = new(@"(\d{3,4})\s*/\s*(\d{1,2})\s*$", RegexOptions.Compiled);

    private static readonly Dictionary<string, GenDate> Cache = new(StringComparer.Ordinal);

    /// <summary>Display style chosen in the preferences.</summary>
    public static DateStyle Style { get; set; } = DateStyle.DMY;

    /// <summary>Read "3/4/1890" as day/month (British) rather than month/day.</summary>
    public static bool DayFirst { get; set; } = true;

    public static readonly GenDate Empty = new("");

    private GenDate(string text) => Text = text;

    public string Text { get; }
    public bool IsEmpty => Text.Length == 0;
    public bool IsValid { get; private init; }
    public DateModifier Modifier { get; private init; }
    public bool IsRange { get; private init; }
    public DatePart Start { get; private init; }
    public DatePart End { get; private init; }

    /// <summary>Sortable key; null when there is no year.</summary>
    public long? SortKey
    {
        get
        {
            if (!IsValid || Start.DayNumber is not long day) return null;
            return Modifier switch
            {
                DateModifier.Before => day * 4 - 1,
                DateModifier.After => day * 4 + 1,
                _ => day * 4,
            };
        }
    }

    public int? Year => IsValid ? Start.Year : null;

    public string Display => IsEmpty ? "" : IsValid ? Format(Style) : Text;

    public override string ToString() => Display;

    public static GenDate Parse(string? text)
    {
        var s = (text ?? "").Trim();
        if (s.Length == 0) return Empty;
        lock (Cache)
        {
            if (Cache.TryGetValue(s, out var cached)) return cached;
            var parsed = ParseCore(s);
            if (Cache.Count > 20000) Cache.Clear();
            Cache[s] = parsed;
            return parsed;
        }
    }

    private static GenDate ParseCore(string s)
    {
        var m = RangeRegex.Match(s);
        if (!m.Success) m = FromToRegex.Match(s);
        if (!m.Success) m = YearRangeRegex.Match(s);
        if (m.Success)
        {
            var a = ParsePart(m.Groups[1].Value);
            var b = ParsePart(m.Groups[2].Value);
            if (a is { HasYear: true } && b is { HasYear: true })
            {
                // "Bet. 3-17 Mar 1890": the first end may borrow the second's month and year.
                var start = a.Value;
                return new GenDate(s) { IsValid = true, IsRange = true, Start = start, End = b.Value };
            }
            if (a is not null && b is { HasYear: true })
            {
                var start = a.Value with { Year = a.Value.Year ?? b.Value.Year, Month = a.Value.Month ?? b.Value.Month };
                return new GenDate(s) { IsValid = true, IsRange = true, Start = start, End = b.Value };
            }
            return new GenDate(s);
        }

        var modifier = DateModifier.None;
        var rest = s;
        var mm = ModifierRegex.Match(s);
        if (mm.Success)
        {
            modifier = mm.Groups[1].Value.ToLowerInvariant() switch
            {
                "bef" or "before" => DateModifier.Before,
                "aft" or "after" => DateModifier.After,
                "cal" or "calc" or "calculated" => DateModifier.Calculated,
                _ => DateModifier.About,
            };
            rest = mm.Groups[2].Value;
        }

        var part = ParsePart(rest);
        if (part is null) return new GenDate(s);
        return new GenDate(s) { IsValid = true, Modifier = modifier, Start = part.Value, End = part.Value };
    }

    private static DatePart? ParsePart(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        var negative = false;
        foreach (var suffix in new[] { "b.c.e.", "b.c.", "bce", "bc" })
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                negative = true;
                s = s[..^suffix.Length].Trim();
                break;
            }
        }
        foreach (var suffix in new[] { "a.d.", "ad", "ce" })
        {
            if (!negative && s.EndsWith(" " + suffix, StringComparison.Ordinal))
            {
                s = s[..^suffix.Length].Trim();
                break;
            }
        }

        int? year = null;
        var doubleDate = false;
        var dm = DoubleYearRegex.Match(s);
        if (dm.Success)
        {
            var first = int.Parse(dm.Groups[1].Value, CultureInfo.InvariantCulture);
            var second = int.Parse(dm.Groups[2].Value, CultureInfo.InvariantCulture);
            var modulus = dm.Groups[2].Value.Length == 1 ? 10 : 100;
            if ((first + 1) % modulus == second)
            {
                year = first + 1;
                doubleDate = true;
                s = s[..dm.Index].Trim();
            }
        }

        var tokens = Regex.Split(s, @"[\s,./\-]+").Where(t => t.Length > 0).ToList();
        int? month = null;
        var numbers = new List<(int Value, int Length)>();
        foreach (var token in tokens)
        {
            if (char.IsLetter(token[0]))
            {
                var found = MonthFromName(token);
                if (found is null || month is not null) return null;
                month = found;
            }
            else if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                numbers.Add((n, token.Length));
            }
            else
            {
                return null;
            }
        }

        int? day = null;
        var small = new List<int>();
        foreach (var (value, length) in numbers)
        {
            if (length >= 3 || value > 31)
            {
                if (year is not null) return null;
                year = value;
            }
            else
            {
                small.Add(value);
            }
        }

        if (month is not null)
        {
            if (small.Count > 1) return null;
            if (small.Count == 1) day = small[0];
        }
        else if (small.Count == 2)
        {
            var (a, b) = (small[0], small[1]);
            if (a > 12) (day, month) = (a, b);
            else if (b > 12) (month, day) = (a, b);
            else if (DayFirst) (day, month) = (a, b);
            else (month, day) = (a, b);
        }
        else if (small.Count == 1)
        {
            // A lone small number with no month is a two-digit year only when nothing else is there.
            if (year is not null) return null;
            year = small[0];
        }
        else if (small.Count > 2)
        {
            return null;
        }

        if (year is null && month is null) return null;
        if (month is < 1 or > 12) return null;
        if (day is not null)
        {
            if (month is null) return null;
            if (day < 1 || day > DaysInMonth(year ?? 2000, month.Value)) return null;
        }
        if (negative && year is not null) year = -year;
        return new DatePart(year, month, day, doubleDate);
    }

    private static int? MonthFromName(string token)
    {
        if (token.Length < 3) return null;
        for (var i = 0; i < 12; i++)
        {
            if (MonthLong[i].ToLowerInvariant().StartsWith(token, StringComparison.Ordinal))
                return i + 1;
        }
        return null;
    }

    public string Format(DateStyle style)
    {
        if (!IsValid) return Text;
        if (IsRange)
        {
            var a = Start;
            var b = End;
            if (!a.DoubleDate && a.Year == b.Year)
            {
                a = a with { Year = null };
                if (a.Month == b.Month && a.Day is not null)
                {
                    if (style is DateStyle.MDY or DateStyle.LongMDY) b = b with { Month = null };
                    else a = a with { Month = null };
                }
            }
            var left = FormatPart(a, DateModifier.None, style);
            var right = FormatPart(b, DateModifier.None, style);
            return $"Bet. {left}-{right}";
        }
        return FormatPart(Start, Modifier, style);
    }

    private static string FormatPart(DatePart p, DateModifier modifier, DateStyle style)
    {
        var mod = modifier switch
        {
            DateModifier.About => "Abt.",
            DateModifier.Before => "Bef.",
            DateModifier.After => "Aft.",
            DateModifier.Calculated => "Cal.",
            _ => "",
        };
        var year = FormatYear(p);
        var month = p.Month is int m ? (style is DateStyle.LongDMY or DateStyle.LongMDY ? MonthLong[m - 1] : MonthAbbrev[m - 1]) : "";
        var day = p.Day is int d ? (style is DateStyle.DMY or DateStyle.LongDMY ? d.ToString("00", CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture)) : "";

        string text;
        if (style is DateStyle.MDY or DateStyle.LongMDY)
        {
            var comma = p.Month is not null && p.Day is not null && p.Year is not null ? "," : "";
            text = Join(mod, month, day + comma, year);
        }
        else
        {
            text = Join(mod, day, month, year);
        }
        return text;
    }

    private static string FormatYear(DatePart p)
    {
        if (p.Year is not int y) return "";
        var abs = Math.Abs(y);
        var era = y < 0 ? " BC" : abs <= 999 ? " AD" : "";
        if (p.DoubleDate)
            return $"{abs - 1}/{abs % 100:00}{era}";
        return $"{abs}{era}";
    }

    private static string Join(params string[] parts) =>
        string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x)));

    /// <summary>The date written for a GEDCOM file ("ABT 1850", "BET 1850 AND 1860").</summary>
    public string ToGedcom()
    {
        if (IsEmpty) return "";
        if (!IsValid) return $"({Text})";
        if (IsRange) return $"BET {GedcomPart(Start)} AND {GedcomPart(End)}";
        var prefix = Modifier switch
        {
            DateModifier.About => "ABT ",
            DateModifier.Before => "BEF ",
            DateModifier.After => "AFT ",
            DateModifier.Calculated => "CAL ",
            _ => "",
        };
        return prefix + GedcomPart(Start);
    }

    private static string GedcomPart(DatePart p)
    {
        var sb = new StringBuilder();
        if (p.Day is int d) sb.Append(d.ToString(CultureInfo.InvariantCulture)).Append(' ');
        if (p.Month is int m) sb.Append(GedcomMonths[m - 1]).Append(' ');
        if (p.Year is int y)
        {
            var abs = Math.Abs(y);
            sb.Append(p.DoubleDate ? $"{abs - 1}/{abs % 100:00}" : abs.ToString(CultureInfo.InvariantCulture));
            if (y < 0) sb.Append(" B.C.");
        }
        return sb.ToString().Trim();
    }

    /// <summary>Reads a GEDCOM date into the text this app keeps.</summary>
    public static string FromGedcom(string value)
    {
        var v = value.Trim();
        if (v.StartsWith('(') && v.EndsWith(')')) return v[1..^1];
        v = Regex.Replace(v, @"\bAND\b", "and", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"\bBET\b", "Bet.", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"\bEST\b", "Abt.", RegexOptions.IgnoreCase);
        v = Regex.Replace(v, @"\bINT\b", "", RegexOptions.IgnoreCase);
        var parsed = Parse(v);
        return parsed.IsValid ? parsed.Format(DateStyle.DMY) : v;
    }

    // ---- calendar arithmetic ----

    public static bool IsLeapYear(int year)
    {
        if (year < 0) year++;
        return year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
    }

    public static int DaysInMonth(int year, int month) =>
        month == 2 && IsLeapYear(year) ? 29 : MonthDays[month];

    /// <summary>Julian day number of a proleptic Gregorian date (no year zero).</summary>
    public static long ToDayNumber(int year, int month, int day)
    {
        if (year < 0) year++;
        var a = (14 - month) / 12;
        long y = year + 4800 - a;
        var m = month + 12 * a - 3;
        return day + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
    }

    public static (int Year, int Month, int Day) FromDayNumber(long jdn)
    {
        var a = jdn + 32044;
        var b = (4 * a + 3) / 146097;
        var c = a - 146097 * b / 4;
        var d = (4 * c + 3) / 1461;
        var e = c - 1461 * d / 4;
        var m = (5 * e + 2) / 153;
        var day = (int)(e - (153 * m + 2) / 5 + 1);
        var month = (int)(m + 3 - 12 * (m / 10));
        var year = (int)(100 * b + d - 4800 + m / 10);
        if (year <= 0) year--;
        return (year, month, day);
    }

    public static string WeekdayName(long jdn) =>
        CultureInfo.InvariantCulture.DateTimeFormat.DayNames[(int)((jdn + 1) % 7)];
}

/// <summary>An age in years, months and days (Design considerations, Dates: Age).</summary>
public readonly record struct Age(int Years, int Months, int Days)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Years != 0) parts.Add(Years == 1 ? "1 year" : $"{Years} years");
        if (Months != 0) parts.Add(Months == 1 ? "1 month" : $"{Months} months");
        if (Days != 0 || parts.Count == 0) parts.Add(Days == 1 ? "1 day" : $"{Days} days");
        return string.Join(", ", parts);
    }

    public static Age Between(int y1, int m1, int d1, int y2, int m2, int d2)
    {
        if (GenDate.ToDayNumber(y2, m2, d2) < GenDate.ToDayNumber(y1, m1, d1))
            (y1, m1, d1, y2, m2, d2) = (y2, m2, d2, y1, m1, d1);

        var years = y2 - y1;
        if (y1 < 0 && y2 > 0) years--;
        var months = m2 >= m1 ? m2 - m1 : 12 + m2 - m1;
        if (d2 < d1) months--;
        if (months < 0) months += 12;
        if (m2 < m1 || (m2 == m1 && d2 < d1)) years--;

        var days = d2 - d1;
        if (days < 0)
        {
            var prevMonth = m2 == 1 ? 12 : m2 - 1;
            var prevYear = m2 == 1 ? y2 - 1 : y2;
            var len = GenDate.DaysInMonth(prevYear, prevMonth);
            days = d1 > len ? d2 : d2 + len - d1;
        }
        return new Age(years, months, days);
    }

    /// <summary>Whole years between two dates, or null when either lacks a year.</summary>
    public static int? YearsBetween(DatePart a, DatePart b)
    {
        if (a.Year is not int y1 || b.Year is not int y2) return null;
        if (a.Month is null || b.Month is null) return Math.Abs(y2 - y1);
        return Between(y1, a.Month.Value, a.Day ?? 1, y2, b.Month.Value, b.Day ?? 1).Years;
    }

    public static (int Year, int Month, int Day) Add(int year, int month, int day, int addYears, int addMonths, int addDays)
    {
        var y = year + addYears;
        if (year < 0 && y >= 0) y++;
        var m = month + addMonths;
        while (m > 12) { m -= 12; y++; if (y == 0) y++; }
        var d = Math.Min(day, GenDate.DaysInMonth(y, m));
        var jdn = GenDate.ToDayNumber(y, m, d) + addDays;
        return GenDate.FromDayNumber(jdn);
    }

    public static (int Year, int Month, int Day) Subtract(int year, int month, int day, int subYears, int subMonths, int subDays)
    {
        var jdn = GenDate.ToDayNumber(year, month, day) - subDays;
        var (y, m, d) = GenDate.FromDayNumber(jdn);
        m -= subMonths;
        while (m < 1) { m += 12; y--; if (y == 0) y--; }
        var ny = y - subYears;
        if (y > 0 && ny <= 0) ny--;
        d = Math.Min(d, GenDate.DaysInMonth(ny, m));
        return (ny, m, d);
    }
}
