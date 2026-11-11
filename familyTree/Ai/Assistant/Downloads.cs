using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Mycelia.Ai.Assistant;

/// <summary>A file the Assistant downloaded: where it is, its size and type, and its SHA-256.</summary>
public sealed record DownloadResult(string Path, string FileName, long Bytes, string? ContentType, string Sha256, bool LooksLikeWebPage);

/// <summary>
/// Downloads for the Assistant's download_file tool (TDD 25): a record image or document at a web address, saved into
/// the tree's media folder (<see cref="Model.TreeDocument.MediaFolder"/>).  Only http and https; never overwrites (a
/// name already there gets " (2)"); written as .part and renamed when complete; at most <see cref="MaxBytes"/>.
/// Ported from multifariousStudio's Core/Assistant/Downloads.cs without its holding folder and clean-up: a file here
/// is the tree's media, so nothing is ever deleted.
/// </summary>
public static class Downloads
{
    public const long MaxBytes = 500L * 1024 * 1024;

    private static HttpClient? _http;

    public static HttpClient Http => _http ??= NewHttp();

    private static HttpClient NewHttp()
    {
        var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15), AllowAutoRedirect = true, MaxAutomaticRedirections = 10 })
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        // The program's name and version, nothing about the person.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Mycelia/{typeof(Downloads).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"}");
        return http;
    }

    public static async Task<DownloadResult> DownloadAsync(HttpClient http, string url, string folder, string? fileName, CancellationToken cancel)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Only web addresses (https:// or http://) can be downloaded.");
        Directory.CreateDirectory(folder);

        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{uri.Host} answered {(int)response.StatusCode} {response.ReasonPhrase}." +
                (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.NotFound
                    ? " The address may need signing in, or may have moved." : string.Empty));
        if (response.Content.Headers.ContentLength is long length && length > MaxBytes)
            throw new InvalidOperationException($"The file is {length / 1048576} MB, more than the {MaxBytes / 1048576} MB limit.");

        string contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        string name = SafeName(fileName) ?? NameFrom(response.Content.Headers.ContentDisposition, response.RequestMessage?.RequestUri ?? uri, contentType);
        if (Path.GetExtension(name).Length == 0 && ExtensionFor(contentType) is { } ext)
            name += ext;
        string part = Path.Combine(folder, $".download-{Guid.NewGuid():N}.part");
        long written = 0;
        string sha;
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var target = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    written += read;
                    if (written > MaxBytes)
                        throw new InvalidOperationException($"The file is more than the {MaxBytes / 1048576} MB limit.");
                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
                sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            string final = MoveToFreeName(part, folder, name);
            // HTML where the address (or the name asked for) doesn't name a web page: likely a sign-in or error page.
            static bool IsPageName(string? text) => text != null
                && (text.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || text.EndsWith(".html", StringComparison.OrdinalIgnoreCase));
            bool webPage = contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                && !IsPageName((response.RequestMessage?.RequestUri ?? uri).AbsolutePath) && !IsPageName(fileName);
            return new DownloadResult(final, Path.GetFileName(final), written, contentType.Length > 0 ? contentType : null, sha, webPage);
        }
        catch
        {
            try { File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Renames the finished download to its name, or "name (2).ext", "name (3).ext"... when that is taken.</summary>
    private static string MoveToFreeName(string part, string folder, string name)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int n = 1; n < 1000; n++)
        {
            string candidate = Path.Combine(folder, n == 1 ? name : $"{stem} ({n}){extension}");
            if (File.Exists(candidate) || Directory.Exists(candidate))
                continue;
            try
            {
                File.Move(part, candidate, overwrite: false);
                return candidate;
            }
            catch (IOException) when (File.Exists(candidate))
            {
                // Taken in the meantime: try the next number.
            }
        }
        throw new IOException("No free file name was found for " + name + ".");
    }

    /// <summary>The file name: Content-Disposition (filename*, then filename), else the address's last part, else "download"; an extension from the content type when it has none.</summary>
    internal static string NameFrom(ContentDispositionHeaderValue? disposition, Uri uri, string contentType)
    {
        string? name = SafeName(disposition?.FileNameStar) ?? SafeName(disposition?.FileName?.Trim('"'));
        if (name == null)
        {
            string last = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? string.Empty).Trim('/');
            name = SafeName(last);
        }
        name ??= "download";
        if (Path.GetExtension(name).Length == 0 && ExtensionFor(contentType) is { } extension)
            name += extension;
        return name;
    }

    /// <summary>A file name with path parts and characters Windows refuses removed; null when nothing is left.</summary>
    internal static string? SafeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        name = name.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        if (name.Length == 0 || name is "." or "..")
            return null;
        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            name = "_" + name;
        return name.Length > 150 ? Path.GetFileNameWithoutExtension(name)[..(150 - Path.GetExtension(name).Length)] + Path.GetExtension(name) : name;
    }

    private static string? ExtensionFor(string contentType) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => ".pdf",
        "application/zip" => ".zip",
        "application/json" => ".json",
        "application/xml" or "text/xml" => ".xml",
        "text/csv" => ".csv",
        "text/plain" => ".txt",
        "text/html" => ".html",
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/tiff" => ".tif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        _ => null
    };
}
