using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Mycelia.Model;

namespace Mycelia.Ui.Charts;

public enum ChartKind { Pedigree, Descendants, Fan, FamilyGroupSheet }

/// <summary>
/// Draws printable charts with vector drawing, so they stay sharp at any zoom, screen scale or print resolution.
/// </summary>
public sealed class ChartCanvas : FrameworkElement
{
    private const double Pad = 40;
    private Size _size = new(800, 600);

    public TreeDocument? Doc { get; set; }
    public Person? Root { get; set; }
    /// <summary>Generations a chart starts with, for every chart type (Design considerations, Charts).</summary>
    public const int DefaultGenerations = 4;

    public ChartKind Kind { get; set; } = ChartKind.Pedigree;
    public int Generations { get; set; } = DefaultGenerations;
    public bool ShowPlaces { get; set; } = true;
    public bool ColourBySex { get; set; } = true;
    public string Title { get; set; } = "";

    // The theme's fonts (Inter, and Space Grotesk for the title), or Segoe UI where there is no application to hold them.
    private static readonly Typeface Regular = new(ThemeFont("UiFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(ThemeFont("UiFont"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface Display = new(ThemeFont("DisplayFont"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static FontFamily ThemeFont(string key) =>
        Application.Current?.TryFindResource(key) as FontFamily ?? new FontFamily("Segoe UI");

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x12, 0x24, 0x1E));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x5B, 0x6A, 0x63));
    private static readonly Brush Line = Frozen(Color.FromRgb(0xA9, 0xB6, 0xB0));
    private static readonly Brush Paper = Frozen(Colors.White);
    private static readonly Brush Brand = Frozen(Color.FromRgb(0x2F, 0x7D, 0x32));
    private static readonly Brush MaleFill = Frozen(Color.FromRgb(0xE7, 0xEF, 0xF9));
    private static readonly Brush FemaleFill = Frozen(Color.FromRgb(0xF8, 0xE8, 0xEF));
    private static readonly Brush NeutralFill = Frozen(Color.FromRgb(0xEE, 0xF2, 0xF0));
    private static readonly Brush MaleEdge = Frozen(Color.FromRgb(0x3D, 0x72, 0xAE));
    private static readonly Brush FemaleEdge = Frozen(Color.FromRgb(0xB2, 0x4E, 0x79));
    private static readonly Brush NeutralEdge = Frozen(Color.FromRgb(0x9A, 0xA6, 0xA1));

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Redraw()
    {
        _size = Kind switch
        {
            ChartKind.Pedigree => PedigreeSize(),
            ChartKind.Descendants => DescendantSize(),
            ChartKind.Fan => FanSize(),
            _ => new Size(794, 1123),
        };
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => _size;

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Paper, null, new Rect(_size));
        if (Doc is null || Root is null) return;
        switch (Kind)
        {
            case ChartKind.Pedigree: DrawPedigree(dc); break;
            case ChartKind.Descendants: DrawDescendants(dc); break;
            case ChartKind.Fan: DrawFan(dc); break;
            case ChartKind.FamilyGroupSheet: DrawFamilyGroup(dc); break;
        }
    }

    // ---------------------------------------------------------------- text helpers

    private double Dip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    private FormattedText Text(string text, double size, Brush brush, Typeface? face = null, double maxWidth = 0)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face ?? Regular, size, brush, Dip)
        {
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 1,
        };
        if (maxWidth > 0) ft.MaxTextWidth = maxWidth;
        return ft;
    }

    private (Brush Fill, Brush Edge) Colours(Person p) => !ColourBySex
        ? (NeutralFill, NeutralEdge)
        : p.Sex switch
        {
            Sex.Male => (MaleFill, MaleEdge),
            Sex.Female => (FemaleFill, FemaleEdge),
            _ => (NeutralFill, NeutralEdge),
        };

    private string Dates(Person p)
    {
        var parts = new List<string>();
        var b = p.Birth?.ParsedDate.Display;
        var d = p.Death?.ParsedDate.Display;
        if (!string.IsNullOrEmpty(b)) parts.Add("b. " + b);
        if (!string.IsNullOrEmpty(d)) parts.Add("d. " + d);
        return string.Join("   ", parts);
    }

    private void DrawTitle(DrawingContext dc, string subtitle)
    {
        dc.DrawText(Text(Title, 22, Ink, Display), new Point(Pad, Pad - 6));
        dc.DrawText(Text(subtitle, 12, Muted), new Point(Pad, Pad + 26));
    }

    private void DrawBox(DrawingContext dc, Person p, Rect r)
    {
        var (fill, edge) = Colours(p);
        dc.DrawRoundedRectangle(fill, new Pen(edge, 1), r, 6, 6);
        dc.DrawRectangle(edge, null, new Rect(r.X, r.Y + 6, 3, r.Height - 12));
        var w = r.Width - 18;
        dc.DrawText(Text(p.DisplayName, 12.5, Ink, Bold, w), new Point(r.X + 11, r.Y + 7));
        var dates = Dates(p);
        if (dates.Length > 0) dc.DrawText(Text(dates, 10.5, Muted, null, w), new Point(r.X + 11, r.Y + 25));
        if (ShowPlaces && r.Height >= 58)
        {
            var place = Doc!.PlaceName(p.Birth?.PlaceId);
            if (place.Length > 0) dc.DrawText(Text(place, 10.5, Muted, null, w), new Point(r.X + 11, r.Y + 40));
        }
    }

    private static void Elbow(DrawingContext dc, Point a, Point b, bool horizontalFirst)
    {
        var pen = new Pen(Line, 1.2);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(a, false, false);
            if (horizontalFirst)
            {
                var mx = (a.X + b.X) / 2;
                ctx.LineTo(new Point(mx, a.Y), true, false);
                ctx.LineTo(new Point(mx, b.Y), true, false);
            }
            else
            {
                var my = (a.Y + b.Y) / 2;
                ctx.LineTo(new Point(a.X, my), true, false);
                ctx.LineTo(new Point(b.X, my), true, false);
            }
            ctx.LineTo(b, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    // ---------------------------------------------------------------- pedigree

    private const double PedW = 230, PedH = 60, PedGapX = 44, PedSlot = 70, Header = 80;

    private Size PedigreeSize()
    {
        var slots = 1 << (Generations - 1);
        return new Size(Pad * 2 + Generations * PedW + (Generations - 1) * PedGapX, Pad * 2 + Header + slots * PedSlot);
    }

    private void DrawPedigree(DrawingContext dc)
    {
        DrawTitle(dc, $"{Generations} generations   ·   {DateTime.Now:d MMMM yyyy}");
        var slots = 1 << (Generations - 1);
        var total = slots * PedSlot;
        var top = Pad + Header;
        var people = new Person?[Generations][];
        for (var g = 0; g < Generations; g++) people[g] = new Person?[1 << g];
        people[0][0] = Root;
        for (var g = 0; g + 1 < Generations; g++)
        for (var i = 0; i < people[g].Length; i++)
        {
            if (people[g][i] is not { } p) continue;
            people[g + 1][2 * i] = Doc!.Father(p);
            people[g + 1][2 * i + 1] = Doc.Mother(p);
        }

        Rect BoxAt(int g, int i)
        {
            var x = Pad + g * (PedW + PedGapX);
            var cy = top + (i + 0.5) * total / (1 << g);
            return new Rect(x, cy - PedH / 2, PedW, PedH);
        }

        for (var g = 0; g + 1 < Generations; g++)
        for (var i = 0; i < people[g].Length; i++)
        {
            if (people[g][i] is null) continue;
            var child = BoxAt(g, i);
            foreach (var k in new[] { 2 * i, 2 * i + 1 })
            {
                if (people[g + 1][k] is null) continue;
                var parent = BoxAt(g + 1, k);
                Elbow(dc, new Point(child.Right, child.Y + child.Height / 2), new Point(parent.Left, parent.Y + parent.Height / 2), true);
            }
        }
        for (var g = 0; g < Generations; g++)
        for (var i = 0; i < people[g].Length; i++)
            if (people[g][i] is { } p) DrawBox(dc, p, BoxAt(g, i));
    }

    // ---------------------------------------------------------------- descendants

    private const double DesW = 190, DesH = 64, DesGapX = 16, DesGapY = 56;

    private double SubtreeWidth(Person p, int level)
    {
        if (level >= Generations - 1) return DesW;
        var kids = Doc!.Children(p).ToList();
        if (kids.Count == 0) return DesW;
        return Math.Max(DesW, kids.Sum(k => SubtreeWidth(k, level + 1)) + DesGapX * (kids.Count - 1));
    }

    private Size DescendantSize()
    {
        if (Doc is null || Root is null) return new Size(800, 600);
        return new Size(Math.Max(700, Pad * 2 + SubtreeWidth(Root, 0)), Pad * 2 + Header + Generations * (DesH + DesGapY));
    }

    private void DrawDescendants(DrawingContext dc)
    {
        DrawTitle(dc, $"{Generations} generations   ·   {DateTime.Now:d MMMM yyyy}");
        var top = Pad + Header;
        var total = SubtreeWidth(Root!, 0);
        var left = Math.Max(Pad, (_size.Width - total) / 2);

        double Layout(Person p, int level, double l)
        {
            var span = SubtreeWidth(p, level);
            var x = l + span / 2 - DesW / 2;
            var y = top + level * (DesH + DesGapY);
            if (level < Generations - 1)
            {
                var kids = Doc!.Children(p).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
                var used = kids.Sum(k => SubtreeWidth(k, level + 1)) + DesGapX * Math.Max(0, kids.Count - 1);
                var cursor = l + (span - used) / 2;
                foreach (var k in kids)
                {
                    var kx = Layout(k, level + 1, cursor);
                    Elbow(dc, new Point(x + DesW / 2, y + DesH), new Point(kx + DesW / 2, y + DesH + DesGapY), false);
                    cursor += SubtreeWidth(k, level + 1) + DesGapX;
                }
            }
            var rect = new Rect(x, y, DesW, DesH);
            DrawBox(dc, p, rect);
            var spouse = Doc!.Spouses(p).FirstOrDefault();
            if (spouse is not null)
                dc.DrawText(Text("& " + spouse.DisplayName, 10.5, Brand, null, DesW - 18), new Point(x + 11, y + 42));
            return x;
        }

        var saveShowPlaces = ShowPlaces;
        ShowPlaces = false;
        Layout(Root!, 0, left);
        ShowPlaces = saveShowPlaces;
    }

    // ---------------------------------------------------------------- fan

    private const double FanRing = 96, FanCentre = 70;

    private Size FanSize()
    {
        var r = FanCentre + (Generations - 1) * FanRing;
        return new Size(Pad * 2 + 2 * r, Pad * 2 + Header + r + 20);
    }

    private void DrawFan(DrawingContext dc)
    {
        DrawTitle(dc, $"{Generations} generations   ·   {DateTime.Now:d MMMM yyyy}");
        var radius = FanCentre + (Generations - 1) * FanRing;
        var centre = new Point(_size.Width / 2, Pad + Header + radius);

        // Root at the centre, each ring one generation further back, father's line on the left.
        var (rf, re) = Colours(Root!);
        dc.DrawEllipse(rf, new Pen(re, 1.2), centre, FanCentre, FanCentre);
        var rootName = Text(Root!.DisplayName, 12.5, Ink, Bold, FanCentre * 1.7);
        rootName.TextAlignment = TextAlignment.Center;
        dc.DrawText(rootName, new Point(centre.X, centre.Y - 34));
        var rootDates = Text(Root.LifeSpan, 10.5, Muted, null, FanCentre * 1.7);
        rootDates.TextAlignment = TextAlignment.Center;
        dc.DrawText(rootDates, new Point(centre.X, centre.Y - 16));
        dc.DrawRectangle(Paper, null, new Rect(centre.X - radius - 2, centre.Y, radius * 2 + 4, FanCentre + 4));

        var level = new List<Person?> { Root };
        for (var g = 1; g < Generations; g++)
        {
            var next = new List<Person?>();
            foreach (var p in level)
            {
                next.Add(p is null ? null : Doc!.Father(p));
                next.Add(p is null ? null : Doc!.Mother(p));
            }
            var inner = FanCentre + (g - 1) * FanRing;
            var outer = inner + FanRing;
            var count = next.Count;
            for (var i = 0; i < count; i++)
            {
                var a0 = Math.PI + Math.PI * i / count;
                var a1 = Math.PI + Math.PI * (i + 1) / count;
                var p = next[i];
                var wedge = Wedge(centre, inner, outer, a0, a1);
                if (p is null)
                {
                    dc.DrawGeometry(null, new Pen(NeutralFill, 1), wedge);
                    continue;
                }
                var (fill, edge) = Colours(p);
                dc.DrawGeometry(fill, new Pen(Paper, 2), wedge);
                DrawWedgeText(dc, p, centre, inner, outer, a0, a1, g);
            }
            level = next;
        }
    }

    private static Geometry Wedge(Point c, double r0, double r1, double a0, double a1)
    {
        Point P(double r, double a) => new(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(P(r0, a0), true, true);
            ctx.LineTo(P(r1, a0), true, false);
            ctx.ArcTo(P(r1, a1), new Size(r1, r1), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true, false);
            ctx.LineTo(P(r0, a1), true, false);
            ctx.ArcTo(P(r0, a0), new Size(r0, r0), 0, a1 - a0 > Math.PI, SweepDirection.Counterclockwise, true, false);
        }
        geo.Freeze();
        return geo;
    }

    private void DrawWedgeText(DrawingContext dc, Person p, Point c, double r0, double r1, double a0, double a1, int g)
    {
        var mid = (a0 + a1) / 2;
        var rm = (r0 + r1) / 2;
        var arc = rm * (a1 - a0);
        var pos = new Point(c.X + rm * Math.Cos(mid), c.Y + rm * Math.Sin(mid));
        if (arc > FanRing * 0.9)
        {
            // Wide wedge: text runs along the ring.
            var deg = mid * 180 / Math.PI + 90;
            dc.PushTransform(new RotateTransform(deg, pos.X, pos.Y));
            var name = Text(p.DisplayName, 12, Ink, Bold, arc - 14);
            name.TextAlignment = TextAlignment.Center;
            dc.DrawText(name, new Point(pos.X, pos.Y - 16));
            var span = Text(p.LifeSpan, 10.5, Muted, null, arc - 14);
            span.TextAlignment = TextAlignment.Center;
            dc.DrawText(span, new Point(pos.X, pos.Y + 1));
            dc.Pop();
        }
        else
        {
            // Narrow wedge: text runs outwards along the radius.
            var deg = mid * 180 / Math.PI;
            var flip = Math.Cos(mid) < 0;
            dc.PushTransform(new RotateTransform(flip ? deg + 180 : deg, pos.X, pos.Y));
            var size = arc > 30 ? 10.5 : 9;
            var name = Text(p.DisplayName, size, Ink, g < 4 ? Bold : Regular, FanRing - 10);
            name.TextAlignment = TextAlignment.Center;
            var lines = arc > 30 ? 2 : 1;
            dc.DrawText(name, new Point(pos.X, pos.Y - (lines == 2 ? size + 2 : size / 2 + 1)));
            if (lines == 2)
            {
                var span = Text(p.LifeSpan, 9, Muted, null, FanRing - 10);
                span.TextAlignment = TextAlignment.Center;
                dc.DrawText(span, new Point(pos.X, pos.Y + 1));
            }
            dc.Pop();
        }
    }

    // ---------------------------------------------------------------- family group sheet

    private void DrawFamilyGroup(DrawingContext dc)
    {
        var doc = Doc!;
        var person = Root!;
        var fam = doc.SpouseFamilies(person).FirstOrDefault();
        var husband = fam is null ? (person.Sex != Sex.Female ? person : null) : doc.PartnerBySex(fam, Sex.Male, 1);
        var wife = fam is null ? (person.Sex == Sex.Female ? person : null) : doc.PartnerBySex(fam, Sex.Female, 2);
        if (fam is not null && husband is null && wife is null) husband = person;
        var width = _size.Width - Pad * 2;
        var y = Pad;

        dc.DrawText(Text("Family Group Sheet", 22, Ink, Display), new Point(Pad, y));
        y += 34;
        dc.DrawText(Text(fam is null ? person.DisplayName : doc.CoupleName(fam), 13, Muted, null, width), new Point(Pad, y));
        y += 30;

        double Section(string heading, Person? p, double top)
        {
            dc.DrawRectangle(Brand, null, new Rect(Pad, top, width, 26));
            dc.DrawText(Text(heading + (p is null ? "" : ":  " + p.DisplayName), 12.5, Paper, Bold, width - 16), new Point(Pad + 8, top + 5));
            top += 30;
            if (p is null) return top + 8;
            var rows = new List<(string, string)>();
            foreach (var t in new[] { FactTypes.Birth, FactTypes.Baptism, FactTypes.Christening, FactTypes.Death, FactTypes.Burial, FactTypes.Occupation })
            {
                var f = p.Preferred(t);
                if (f is null) continue;
                rows.Add((f.Type.Name, Describe(f)));
            }
            rows.Add(("Father", doc.Father(p)?.DisplayName ?? ""));
            rows.Add(("Mother", doc.Mother(p)?.DisplayName ?? ""));
            foreach (var (label, value) in rows)
            {
                dc.DrawText(Text(label, 11, Muted), new Point(Pad + 8, top + 4));
                dc.DrawText(Text(value, 11.5, Ink, null, width - 130), new Point(Pad + 120, top + 3));
                top += 22;
                dc.DrawRectangle(NeutralFill, null, new Rect(Pad, top, width, 1));
            }
            return top + 14;
        }

        y = Section("Husband", husband, y);
        if (fam?.Marriage is { } m)
        {
            dc.DrawText(Text("Marriage", 11, Muted), new Point(Pad + 8, y));
            dc.DrawText(Text(Describe(m), 11.5, Ink, null, width - 130), new Point(Pad + 120, y - 1));
            y += 30;
        }
        y = Section("Wife", wife, y);

        var kids = fam is null ? doc.Children(person).ToList() : doc.Children(fam).OrderBy(c => c.Birth?.ParsedDate.SortKey ?? long.MaxValue).ToList();
        dc.DrawRectangle(Brand, null, new Rect(Pad, y, width, 26));
        dc.DrawText(Text($"Children ({kids.Count})", 12.5, Paper, Bold), new Point(Pad + 8, y + 5));
        y += 32;
        var n = 1;
        foreach (var k in kids)
        {
            if (y > _size.Height - Pad - 40) break;
            var (fill, edge) = Colours(k);
            dc.DrawEllipse(fill, new Pen(edge, 1), new Point(Pad + 14, y + 9), 9, 9);
            var num = Text(n.ToString(CultureInfo.InvariantCulture), 10, Ink, Bold);
            num.TextAlignment = TextAlignment.Center;
            dc.DrawText(num, new Point(Pad + 14, y + 2));
            dc.DrawText(Text(k.DisplayName, 12, Ink, Bold, width - 40), new Point(Pad + 32, y));
            y += 19;
            var lines = new List<string>();
            if (k.Birth is { } b) lines.Add("Born: " + Describe(b));
            if (k.Death is { } d) lines.Add("Died: " + Describe(d));
            var sp = doc.Spouses(k).FirstOrDefault();
            if (sp is not null) lines.Add("Spouse: " + sp.DisplayName);
            foreach (var line in lines)
            {
                dc.DrawText(Text(line, 11, Muted, null, width - 40), new Point(Pad + 32, y));
                y += 17;
            }
            y += 10;
            n++;
        }
        dc.DrawText(Text($"Prepared {DateTime.Now:d MMMM yyyy}", 10, Muted), new Point(Pad, _size.Height - Pad));
    }

    private string Describe(Fact f)
    {
        var place = Doc!.PlaceName(f.PlaceId);
        var date = f.ParsedDate.Display;
        var parts = new[] { date, place, f.TypeId == FactTypes.Occupation ? f.Description : "" }.Where(s => s.Length > 0);
        return string.Join(", ", parts);
    }
}
