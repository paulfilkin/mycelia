using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Mycelia.Ui;

/// <summary>
/// An assistant reply: read-only and selectable, with the simple Markdown models write shown formatted: **bold**,
/// *italic*, `code`, # headings, - and 1. lists, | tables |, [links](https://...) and bare https:// addresses, which open
/// in the browser.  Only http and https links are opened.  Ported from multifariousStudio's Controls/ChatText.cs with
/// this app's theme keys.
/// </summary>
public sealed class ChatText : RichTextBox
{
    public ChatText(string markdown)
    {
        IsReadOnly = true;
        IsDocumentEnabled = true;
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Padding = new Thickness(0);
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        AddHandler(Hyperlink.RequestNavigateEvent, new System.Windows.Navigation.RequestNavigateEventHandler((_, e) =>
        {
            if (e.Uri.Scheme is "http" or "https")
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }));
        Render(markdown);
        _markdown = markdown ?? string.Empty;
    }

    public ChatText() : this(string.Empty)
    {
    }

    private string _markdown;

    /// <summary>The text shown, as Markdown; setting it draws it again (the MCP Helper's steps arrive later).</summary>
    public string Markdown
    {
        get => _markdown;
        set
        {
            _markdown = value ?? string.Empty;
            Render(_markdown);
        }
    }

    private void Render(string markdown)
    {
        var document = new FlowDocument { PagePadding = new Thickness(0), FontFamily = UiKit.Res<FontFamily>("UiFont"), FontSize = 13 };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Ink");
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        List? list = null;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();

            if (IsTableRow(trimmed))
            {
                var rows = new List<string>();
                while (i < lines.Length && IsTableRow(lines[i].Trim()))
                    rows.Add(lines[i++].Trim());
                i--;
                list = null;
                document.Blocks.Add(BuildTable(rows));
                continue;
            }

            var bullet = Regex.Match(line, @"^\s*(?:[-*•]|\d+[.)])\s+(.*)$");
            if (bullet.Success)
            {
                if (list == null)
                {
                    list = new List
                    {
                        MarkerStyle = Regex.IsMatch(line, @"^\s*\d") ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                        Margin = new Thickness(0, 2, 0, 4),
                        Padding = new Thickness(18, 0, 0, 0)
                    };
                    document.Blocks.Add(list);
                }
                var item = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                AddInlines(item.Inlines, bullet.Groups[1].Value);
                list.ListItems.Add(new ListItem(item));
                continue;
            }
            list = null;

            if (trimmed.Length == 0)
                continue;
            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
            if (heading.Success)
            {
                paragraph.FontWeight = FontWeights.SemiBold;
                paragraph.Margin = new Thickness(0, 4, 0, 4);
                AddInlines(paragraph.Inlines, heading.Groups[2].Value);
            }
            else
            {
                AddInlines(paragraph.Inlines, trimmed);
                // Lines of one paragraph that the model broke by hand stay together.
                while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !IsTableRow(lines[i + 1].Trim())
                    && !Regex.IsMatch(lines[i + 1], @"^\s*(?:[-*•]|\d+[.)])\s+|^\s*#{1,6}\s"))
                {
                    paragraph.Inlines.Add(new LineBreak());
                    AddInlines(paragraph.Inlines, lines[++i].Trim());
                }
            }
            document.Blocks.Add(paragraph);
        }
        if (document.Blocks.LastBlock is Block last)
            last.Margin = new Thickness(last.Margin.Left, last.Margin.Top, last.Margin.Right, 0);
        Document = document;
    }

    private static bool IsTableRow(string line) => line.StartsWith('|') && line.Length > 1 && line.IndexOf('|', 1) > 0;

    private static bool IsSeparator(string row) => Regex.IsMatch(row, @"^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$");

    private static string[] Cells(string row) =>
        row.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();

    private static Table BuildTable(List<string> rows)
    {
        var data = rows.Where(r => !IsSeparator(r)).Select(Cells).ToList();
        bool hasHeader = rows.Count > 1 && IsSeparator(rows[1]);
        int columns = data.Count == 0 ? 1 : data.Max(r => r.Length);
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 8), BorderThickness = new Thickness(1, 1, 0, 0) };
        table.SetResourceReference(Table.BorderBrushProperty, "Border");
        for (int c = 0; c < columns; c++)
            table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (int r = 0; r < data.Count; r++)
        {
            var row = new TableRow();
            bool header = hasHeader && r == 0;
            if (header)
                row.SetResourceReference(TableRow.BackgroundProperty, "SurfaceAlt");
            for (int c = 0; c < columns; c++)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                if (header)
                    paragraph.FontWeight = FontWeights.SemiBold;
                AddInlines(paragraph.Inlines, c < data[r].Length ? data[r][c] : string.Empty);
                var cell = new TableCell(paragraph) { Padding = new Thickness(6, 3, 6, 3), BorderThickness = new Thickness(0, 0, 1, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "Border");
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>
    /// [text](url), bare http(s) addresses, **bold**, *italic*, `code`.  Italic needs text right inside both asterisks and
    /// no letter or asterisk outside them, so a "* " bullet, "2 * 3" or a_b*c isn't read as italic.
    /// </summary>
    private static readonly Regex InlinePattern = new(
        @"\[(?<text>[^\]]+)\]\((?<url>https?://[^\s)]+)\)|(?<bare>https?://[^\s<>()\[\]]+[^\s<>()\[\].,;:!?'""])|\*\*(?<bold>.+?)\*\*|(?<![*\w])\*(?<italic>[^*\s](?:[^*]*?[^*\s])?)\*(?![*\w])|`(?<code>[^`]+)`",
        RegexOptions.Compiled);

    /// <summary>
    /// Puts an answer on the clipboard as it looks here: HTML (Outlook, Word and most editors paste it with its bold,
    /// lists, tables and links) and the same text without Markdown marks for places that take plain text.
    /// </summary>
    public static void CopyFormatted(string markdown)
    {
        var data = new DataObject();
        data.SetData(DataFormats.Html, ClipboardHtml(MarkdownHtml.ToHtml(markdown)));
        data.SetData(DataFormats.UnicodeText, MarkdownHtml.ToPlainText(markdown));
        Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>The Windows "HTML Format" clipboard text: a header with the UTF-8 byte offsets of the HTML and the fragment.</summary>
    public static string ClipboardHtml(string fragment)
    {
        const string header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string before = "<html><body>\r\n<!--StartFragment-->";
        const string after = "<!--EndFragment-->\r\n</body></html>";
        var utf8 = System.Text.Encoding.UTF8;
        int headerLength = utf8.GetByteCount(string.Format(header, 0, 0, 0, 0));
        int startHtml = headerLength;
        int startFragment = startHtml + utf8.GetByteCount(before);
        int endFragment = startFragment + utf8.GetByteCount(fragment);
        int endHtml = endFragment + utf8.GetByteCount(after);
        return string.Format(header, startHtml, endHtml, startFragment, endFragment) + before + fragment + after;
    }

    private static void AddInlines(InlineCollection inlines, string text)
    {
        int at = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > at)
                inlines.Add(new Run(text[at..m.Index]));
            if (m.Groups["url"].Success || m.Groups["bare"].Success)
            {
                string url = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["bare"].Value;
                string shown = m.Groups["text"].Success ? m.Groups["text"].Value : url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    var link = new Hyperlink(new Run(shown)) { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
                    link.SetResourceReference(TextElement.ForegroundProperty, "Brand");
                    inlines.Add(link);
                }
                else
                {
                    inlines.Add(new Run(m.Value));
                }
            }
            else if (m.Groups["bold"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, m.Groups["bold"].Value);
                inlines.Add(bold);
            }
            else if (m.Groups["italic"].Success)
            {
                var italic = new Italic();
                AddInlines(italic.Inlines, m.Groups["italic"].Value);
                inlines.Add(italic);
            }
            else if (m.Groups["code"].Success)
            {
                var code = new Run(m.Groups["code"].Value) { FontFamily = new FontFamily("Cascadia Mono, Consolas") };
                code.SetResourceReference(TextElement.BackgroundProperty, "SurfaceAlt");
                inlines.Add(code);
            }
            at = m.Index + m.Length;
        }
        if (at < text.Length)
            inlines.Add(new Run(text[at..]));
    }

    internal static Regex Inline => InlinePattern;
    internal static bool IsMarkdownTableRow(string line) => IsTableRow(line);
    internal static bool IsMarkdownTableSeparator(string row) => IsSeparator(row);
    internal static string[] TableCells(string row) => Cells(row);
}

/// <summary>
/// The same simple Markdown the Assistant shows (paragraphs, # headings, - and 1. lists, | tables |, **bold**, *italic*, `code`,
/// links), written as HTML with inline styles for pasting into an email or document, or as plain text.
/// </summary>
public static class MarkdownHtml
{
    private const string Font = "font-family:'Segoe UI',Calibri,Arial,sans-serif;font-size:11pt";

    public static string ToHtml(string markdown)
    {
        var html = new System.Text.StringBuilder($"<div style=\"{Font}\">");
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        string? openList = null;
        void CloseList()
        {
            if (openList != null)
                html.Append($"</{openList}>");
            openList = null;
        }
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string trimmed = line.Trim();
            if (ChatText.IsMarkdownTableRow(trimmed))
            {
                CloseList();
                var rows = new List<string>();
                while (i < lines.Length && ChatText.IsMarkdownTableRow(lines[i].Trim()))
                    rows.Add(lines[i++].Trim());
                i--;
                bool hasHeader = rows.Count > 1 && ChatText.IsMarkdownTableSeparator(rows[1]);
                var data = rows.Where(r => !ChatText.IsMarkdownTableSeparator(r)).Select(ChatText.TableCells).ToList();
                int columns = data.Count == 0 ? 1 : data.Max(r => r.Length);
                html.Append("<table style=\"border-collapse:collapse;margin:4px 0 8px 0\">");
                for (int r = 0; r < data.Count; r++)
                {
                    bool header = hasHeader && r == 0;
                    html.Append("<tr>");
                    for (int c = 0; c < columns; c++)
                    {
                        string tag = header ? "th" : "td";
                        string style = $"border:1px solid #c8c8c8;padding:3px 8px;text-align:left;{(header ? "background:#f0f0f0;font-weight:600;" : string.Empty)}{Font}";
                        html.Append($"<{tag} style=\"{style}\">{Inline(c < data[r].Length ? data[r][c] : string.Empty)}</{tag}>");
                    }
                    html.Append("</tr>");
                }
                html.Append("</table>");
                continue;
            }
            var bullet = Regex.Match(line, @"^\s*(?:([-*•])|\d+[.)])\s+(.*)$");
            if (bullet.Success)
            {
                string kind = bullet.Groups[1].Success ? "ul" : "ol";
                if (openList != kind)
                {
                    CloseList();
                    html.Append($"<{kind} style=\"margin:2px 0 6px 0\">");
                    openList = kind;
                }
                html.Append($"<li>{Inline(bullet.Groups[2].Value)}</li>");
                continue;
            }
            CloseList();
            if (trimmed.Length == 0)
                continue;
            var heading = Regex.Match(trimmed, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                html.Append($"<p style=\"margin:6px 0 4px 0;font-weight:600\">{Inline(heading.Groups[2].Value)}</p>");
                continue;
            }
            var paragraph = new System.Text.StringBuilder(Inline(trimmed));
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !ChatText.IsMarkdownTableRow(lines[i + 1].Trim())
                && !Regex.IsMatch(lines[i + 1], @"^\s*(?:[-*•]|\d+[.)])\s+|^\s*#{1,6}\s"))
                paragraph.Append("<br>").Append(Inline(lines[++i].Trim()));
            html.Append($"<p style=\"margin:0 0 6px 0\">{paragraph}</p>");
        }
        CloseList();
        return html.Append("</div>").ToString();
    }

    private static string Inline(string text)
    {
        var html = new System.Text.StringBuilder();
        int at = 0;
        foreach (Match m in ChatText.Inline.Matches(text))
        {
            if (m.Index > at)
                html.Append(System.Net.WebUtility.HtmlEncode(text[at..m.Index]));
            if (m.Groups["url"].Success || m.Groups["bare"].Success)
            {
                string url = m.Groups["url"].Success ? m.Groups["url"].Value : m.Groups["bare"].Value;
                string shown = m.Groups["text"].Success ? m.Groups["text"].Value : url;
                html.Append($"<a href=\"{System.Net.WebUtility.HtmlEncode(url)}\">{System.Net.WebUtility.HtmlEncode(shown)}</a>");
            }
            else if (m.Groups["bold"].Success)
                html.Append($"<b>{Inline(m.Groups["bold"].Value)}</b>");
            else if (m.Groups["italic"].Success)
                html.Append($"<i>{Inline(m.Groups["italic"].Value)}</i>");
            else if (m.Groups["code"].Success)
                html.Append($"<code style=\"font-family:Consolas,'Cascadia Mono',monospace;background:#f0f0f0\">{System.Net.WebUtility.HtmlEncode(m.Groups["code"].Value)}</code>");
            at = m.Index + m.Length;
        }
        if (at < text.Length)
            html.Append(System.Net.WebUtility.HtmlEncode(text[at..]));
        return html.ToString();
    }

    /// <summary>The text without Markdown marks: links as "text (address)", bullets as "•", table cells separated by tabs.</summary>
    public static string ToPlainText(string markdown)
    {
        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Split('\n')
            .Where(l => !ChatText.IsMarkdownTableSeparator(l.Trim()) || !ChatText.IsMarkdownTableRow(l.Trim()))
            .Select(l =>
            {
                string t = l.TrimEnd();
                if (ChatText.IsMarkdownTableRow(t.Trim()))
                    return string.Join("\t", ChatText.TableCells(t).Select(PlainInline));
                t = Regex.Replace(t, @"^(\s*)[-*]\s+", "$1• ");
                t = Regex.Replace(t, @"^\s*#{1,6}\s+", string.Empty);
                return PlainInline(t);
            });
        return string.Join("\r\n", lines).Trim();
    }

    private static string PlainInline(string text) => ChatText.Inline.Replace(text, m =>
        m.Groups["url"].Success ? $"{m.Groups["text"].Value} ({m.Groups["url"].Value})"
        : m.Groups["bare"].Success ? m.Groups["bare"].Value
        : m.Groups["bold"].Success ? PlainInline(m.Groups["bold"].Value)
        : m.Groups["italic"].Success ? PlainInline(m.Groups["italic"].Value)
        : m.Groups["code"].Value);
}
