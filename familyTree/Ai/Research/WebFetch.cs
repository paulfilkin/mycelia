using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Mycelia.Ai.Research;

/// <summary>What fetch_url got: the status, the type, the final address and the text (whole, before any slice is taken).</summary>
public sealed record FetchResult(int Status, string Reason, string ContentType, string FinalUrl, string Text, bool IsHtml, bool Cut)
{
    /// <summary>The page is a search form that ran no search, for what the address asked or at all (<see cref="WebFetch.FormNotRun"/>).</summary>
    public bool FormNotRun { get; init; }
}

/// <summary>
/// fetch_url's requests (TDD 25.4): GET only, http and https only, text, JSON or XML only, at most <see cref="MaxBytes"/>.
/// Redirects are followed here, not by the HTTP client, so a call allowed because its address is a listed archive API
/// stops at a redirect to anywhere else.  A listed API's key is added for its own host over https only, and every
/// key is taken out of what comes back, so the model never sees one.
/// </summary>
public static class WebFetch
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private const int MaxRedirects = 5;

    private static HttpClient? _http;

    public static HttpClient Http => _http ??= NewHttp();

    private static HttpClient NewHttp()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
        })
        { Timeout = TimeSpan.FromSeconds(45) };
        // The program's name and version, nothing about the person.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Mycelia/{typeof(WebFetch).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"}");
        return http;
    }

    /// <summary>The address as the model gave it, or why it can't be fetched.</summary>
    public static Uri Parse(string? url)
    {
        if (url is null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new ArgumentException("url must be a web address starting with https:// or http://.");
        if (uri.UserInfo.Length > 0)
            throw new ArgumentException("A web address with a user name or password in it isn't fetched.");
        return uri;
    }

    /// <summary>The address is one of the listed archive APIs, so the call may run without asking.</summary>
    public static bool IsListed(string? url) =>
        Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && Listed(uri);

    /// <summary>A listed archive API over https.</summary>
    public static bool Listed(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && ResearchApis.For(uri) is not null;

    /// <summary>
    /// Fetches the address.  <paramref name="keyFor"/> gives the stored key for an API id (empty when none).
    /// With <paramref name="stayWithin"/>, a redirect to an address it refuses is not followed (a call that ran without
    /// asking stays among the listed APIs and the sites the person always allows); null follows any redirect.
    /// </summary>
    public static async Task<FetchResult> GetAsync(HttpClient http, Uri uri, Func<string, string> keyFor, Func<Uri, bool>? stayWithin, CancellationToken cancel)
    {
        var secrets = new List<string>();
        for (int hop = 0; ; hop++)
        {
            var api = ResearchApis.For(uri);
            if (stayWithin != null && !stayWithin(uri))
                throw new RedirectException(uri);
            using var request = new HttpRequestMessage(HttpMethod.Get, Prepare(uri, api, keyFor, secrets, out var header));
            request.Headers.Accept.ParseAdd("application/json, application/xml;q=0.9, text/*;q=0.8, */*;q=0.5");
            if (api?.Headers is { } fixedHeaders)
                foreach (var (name, value) in fixedHeaders)
                {
                    request.Headers.Remove(name);
                    request.Headers.TryAddWithoutValidation(name, value);
                }
            if (header is { } h)
                request.Headers.TryAddWithoutValidation(h.Name, h.Value);

            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
            int status = (int)response.StatusCode;
            if (status is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                if (hop >= MaxRedirects)
                    throw new InvalidOperationException($"{uri.Host} redirected more than {MaxRedirects} times.");
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                if (next.Scheme != Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttp)
                    throw new InvalidOperationException($"{uri.Host} redirected to an address that isn't a web address.");
                uri = new Uri(Redact(next.ToString(), secrets));
                continue;
            }

            string type = response.Content.Headers.ContentType?.MediaType ?? "";
            // A longer answer (a book's text) is read to MaxBytes and marked as cut.
            if (type.Length > 0 && !IsText(type))
                throw new InvalidOperationException($"{uri.Host} answered with a {type} file, not text.  download_file saves files such as images and PDFs into the tree's media folder.");

            var (bytes, cut) = await ReadAsync(response, cancel);
            var text = Decode(bytes, response.Content.Headers.ContentType?.CharSet);
            bool html = type.Contains("html", StringComparison.OrdinalIgnoreCase) || (type.Length == 0 && text.TrimStart().StartsWith('<') && text.Contains("<html", StringComparison.OrdinalIgnoreCase));
            var notRun = false;
            if (html)
            {
                var page = text;
                text = HtmlToText(text);
                notRun = FormNotRun(page, text, uri);
            }
            return new FetchResult(status, response.ReasonPhrase ?? "", type, Redact(uri.ToString(), secrets), Redact(text, secrets), html, cut) { FormNotRun = notRun };
        }
    }

    private static readonly Regex InputTag = new(@"<input\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Attribute = new(@"\b(name|type)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A search form that ran no search: the page has a form with a text box named like one of the address's query
    /// parameters, and none of that parameter's words appear in the page's visible text.  A site whose search needs
    /// its form submitted (FreeBMD's posts it) gives back its empty form for such an address; a real results page shows
    /// the words searched for.  From a test on 15 November 2026, in which the model read FreeBMD's form ten times and
    /// reported searches that never ran (TDD 25.4).  An address with no query is checked by <see cref="BareSearchForm"/>.
    /// </summary>
    public static bool FormNotRun(string html, string visibleText, Uri requested)
    {
        if (html.IndexOf("<form", StringComparison.OrdinalIgnoreCase) < 0) return false;
        if (requested.Query.Length < 2) return BareSearchForm(html);
        var query = requested.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => Uri.UnescapeDataString(p[0]).Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Uri.UnescapeDataString(g.First()[1].Replace('+', ' ')), StringComparer.OrdinalIgnoreCase);
        var textBoxes = InputTag.Matches(html).Select(m =>
        {
            string? name = null, kind = null;
            foreach (Match a in Attribute.Matches(m.Value))
            {
                var value = a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value;
                if (a.Groups[1].Value.Equals("name", StringComparison.OrdinalIgnoreCase)) name = value; else kind = value;
            }
            return (Name: name, Kind: kind ?? "text");
        }).Where(t => t.Name is not null && t.Kind is "text" or "search").Select(t => t.Name!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var searched = query.Where(q => textBoxes.Contains(q.Key))
            .Select(q => Regex.Matches(q.Value, @"\p{L}{3,}").Select(m => m.Value).ToList())
            .Where(words => words.Count > 0).ToList();
        if (searched.Count == 0) return false;
        return !searched.SelectMany(w => w).Any(word => visibleText.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    private static readonly Regex FormBlock = new(@"<form\b([^>]*)>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex PostMethod = new(@"\bmethod\s*=\s*[""']?post\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// A search form read with no search in the address: a form that sends by POST, with a text box and a list to choose
    /// from, and no password box or text area (a sign-in, comment or contact form).  Reading it searched nothing.  From a
    /// test on 15 November 2026, in which the model read FreeBMD's form at its bare address and logged FreeBMD as searched.
    /// </summary>
    private static bool BareSearchForm(string html) =>
        FormBlock.Matches(html).Any(f =>
        {
            if (!PostMethod.IsMatch(f.Groups[1].Value)) return false;
            var body = f.Groups[2].Value;
            if (body.Contains("<textarea", StringComparison.OrdinalIgnoreCase) || !body.Contains("<select", StringComparison.OrdinalIgnoreCase)) return false;
            var kinds = InputTag.Matches(body).Select(m => Attribute.Matches(m.Value)
                .Where(a => a.Groups[1].Value.Equals("type", StringComparison.OrdinalIgnoreCase))
                .Select(a => (a.Groups[2].Success ? a.Groups[2].Value : a.Groups[3].Success ? a.Groups[3].Value : a.Groups[4].Value).ToLowerInvariant())
                .FirstOrDefault() ?? "text").ToList();
            return !kinds.Contains("password") && kinds.Any(k => k is "text" or "search");
        });

    /// <summary>The address with the API's fixed parameters, and its key when it goes in the address; or the header to add.</summary>
    private static Uri Prepare(Uri uri, ResearchApi? api, Func<string, string> keyFor, List<string> secrets, out (string Name, string Value)? header)
    {
        header = null;
        if (api is null)
            return uri;
        var added = new List<(string, string)>();
        if (api.Parameters is { } fixedParameters)
            added.AddRange(fixedParameters.Select(p => (p.Key, p.Value)));
        // A key only over https, and only to its own API's host.
        if (api.NeedsKey && uri.Scheme == Uri.UriSchemeHttps && keyFor(api.Id) is { Length: > 0 } key)
        {
            secrets.Add(key);
            if (api.KeyPlace == KeyPlace.Header)
                header = (api.KeyField, key);
            else
                added.Add((api.KeyField, key));
        }
        if (added.Count == 0)
            return uri;
        // Any value the model wrote for these is replaced.
        var names = added.Select(a => a.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !names.Contains(Uri.UnescapeDataString(p.Split('=')[0])));
        var query = string.Join('&', kept.Concat(added.Select(a => $"{Uri.EscapeDataString(a.Item1)}={Uri.EscapeDataString(a.Item2)}")));
        return new UriBuilder(uri) { Query = query }.Uri;
    }

    /// <summary>Thrown when a call allowed for a listed API was redirected elsewhere; the address is given back to the model.</summary>
    public sealed class RedirectException(Uri target) : Exception($"Redirected to {target}")
    {
        public Uri Target { get; } = target;
    }

    private static bool IsText(string type) =>
        type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || type.Contains("json", StringComparison.OrdinalIgnoreCase)
        || type.Contains("xml", StringComparison.OrdinalIgnoreCase);

    private static async Task<(byte[] Bytes, bool Cut)> ReadAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            int take = (int)Math.Min(read, MaxBytes - buffer.Length);
            buffer.Write(chunk, 0, take);
            if (buffer.Length >= MaxBytes)
                return (buffer.ToArray(), true);
        }
        return (buffer.ToArray(), false);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(charset))
                return Encoding.GetEncoding(charset.Trim('"')).GetString(bytes);
        }
        catch (ArgumentException)
        {
            // An unknown character set: UTF-8 below.
        }
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Every key taken out of a text, so none reaches the model (an API may echo the address or the key).</summary>
    public static string Redact(string text, IEnumerable<string> secrets)
    {
        foreach (var secret in secrets.Where(s => s.Length > 0))
        {
            text = text.Replace(secret, "[key]", StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (escaped != secret)
                text = text.Replace(escaped, "[key]", StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>A web page as plain text: scripts, styles and navigation left out.  Ported from multifariousStudio's McpDocs.</summary>
    public static string HtmlToText(string html)
    {
        string text = Regex.Replace(html, @"<(script|style|noscript|svg|nav|footer|head)\b[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<(br|/p|/div|/li|/h[1-6]|/tr|li)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t\r\f\v]+", " ");
        text = Regex.Replace(text, @" *\n *", "\n");
        text = Regex.Replace(text, @"\n\s*\n+", "\n");
        return text.Trim();
    }
}
