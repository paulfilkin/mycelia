using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mycelia.Updates;

/// <summary>Semantic version per semver.org 2.0.0; System.Version cannot order pre-releases such as 0.3.1-beta.1.</summary>
public sealed partial class SemanticVersion : IComparable<SemanticVersion>
{
    // The same pattern as the plugin's signing tool ($MfuSemverPattern) and MFU_Semver.
    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")]
    private static partial Regex Pattern();

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public string[] PreRelease { get; }
    public string Original { get; }

    private SemanticVersion(int major, int minor, int patch, string[] preRelease, string original)
    {
        Major = major; Minor = minor; Patch = patch; PreRelease = preRelease; Original = original;
    }

    public static bool TryParse(string? text, out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var m = Pattern().Match(text.Trim());
        if (!m.Success)
            return false;
        if (!int.TryParse(m.Groups[1].Value, out int major) || !int.TryParse(m.Groups[2].Value, out int minor) || !int.TryParse(m.Groups[3].Value, out int patch))
            return false;
        version = new SemanticVersion(major, minor, patch, m.Groups[4].Success ? m.Groups[4].Value.Split('.') : Array.Empty<string>(), text.Trim());
        return true;
    }

    public static SemanticVersion Parse(string text) => TryParse(text, out var v) ? v! : throw new FormatException($"Not a semantic version: {text}");

    public bool IsPreRelease => PreRelease.Length > 0;

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
            return 1;
        if (Major != other.Major)
            return Major.CompareTo(other.Major);
        if (Minor != other.Minor)
            return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch)
            return Patch.CompareTo(other.Patch);

        // A release is higher than any of its pre-releases; build metadata (+...) is ignored.
        if (PreRelease.Length == 0 && other.PreRelease.Length == 0)
            return 0;
        if (PreRelease.Length == 0)
            return 1;
        if (other.PreRelease.Length == 0)
            return -1;

        int shared = Math.Min(PreRelease.Length, other.PreRelease.Length);
        for (int i = 0; i < shared; i++)
        {
            string a = PreRelease[i], b = other.PreRelease[i];
            if (a == b)
                continue;
            bool aNum = a.All(char.IsAsciiDigit), bNum = b.All(char.IsAsciiDigit);
            if (aNum && bNum)
            {
                // Numeric identifiers have no leading zeros, so the longer one is larger.
                if (a.Length != b.Length)
                    return a.Length.CompareTo(b.Length);
                return string.CompareOrdinal(a, b) < 0 ? -1 : 1;
            }
            if (aNum)
                return -1;   // numeric identifiers rank below alphanumeric ones
            if (bNum)
                return 1;
            return string.CompareOrdinal(a, b) < 0 ? -1 : 1;
        }
        return PreRelease.Length.CompareTo(other.PreRelease.Length);
    }

    public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
    public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
    public override string ToString() => Original;

    /// <summary>The running program's version: &lt;Version&gt; in the csproj, read from AssemblyInformationalVersion.</summary>
    public static SemanticVersion Current { get; } = Of(typeof(SemanticVersion).Assembly);

    public static SemanticVersion Of(Assembly assembly)
    {
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return TryParse(text, out var v) ? v! : Parse("0.0.0");
    }
}

/// <summary>One release as the plugin's latest-release endpoint describes it (multifariousUpdates, MFU_Rest::format_release).</summary>
public sealed class UpdateInfo
{
    public string Product { get; set; } = "";
    public string Name { get; set; } = "";
    public string Channel { get; set; } = "";
    public string ReleaseChannel { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTimeOffset? PublishedAt { get; set; }
    public string? MinimumVersion { get; set; }
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Signature { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string Notes { get; set; } = "";
    public string? NotesUrl { get; set; }
}

public enum UpdateOutcome { UpToDate, NoRelease, Available, Required, Failed }

public sealed record UpdateCheckResult(UpdateOutcome Outcome, UpdateInfo? Info, string? Error);

/// <summary>
/// Software updates from the multifariousUpdates WordPress plugin: ask the latest-release endpoint, download the
/// installer and verify it.  A file is only handed back when its size, SHA-256 and ECDSA P-256 signature all match;
/// the signature is what makes a compromised server harmless (the private key never goes on the server).  Ported from
/// multifariousStudio's Core/UpdateClient.cs unchanged apart from the namespace (TDD 23).
/// </summary>
public sealed class UpdateClient
{
    public const string MessageHeader = "multifarious-update-v1";
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;

    public Uri Endpoint { get; }
    public string ProductSlug { get; }
    public IReadOnlyDictionary<string, string> PublicKeys { get; }
    public SemanticVersion Current { get; }

    public UpdateClient(string endpoint, string productSlug, IReadOnlyDictionary<string, string> publicKeys, SemanticVersion current, string productName, HttpClient? http = null)
    {
        Endpoint = new Uri(endpoint);
        if (!IsSecure(Endpoint))
            throw new ArgumentException("The update endpoint must use HTTPS.", nameof(endpoint));
        ProductSlug = productSlug;
        PublicKeys = publicKeys;
        Current = current;
        _http = http ?? NewHttp(productName, current);
    }

    private static HttpClient NewHttp(string productName, SemanticVersion current)
    {
        var http = new HttpClient(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = TimeSpan.FromMinutes(10) };
        // The product's own name and version, never anything about the person.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(productName, current.ToString()));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>HTTPS, or plain HTTP to this computer only (the check programs' stand-in server).</summary>
    private static bool IsSecure(Uri uri) => uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    /// <summary>Asks the endpoint for the newest release on a channel and compares it with the running version.</summary>
    public async Task<UpdateCheckResult> CheckAsync(string channel, CancellationToken cancel = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var info = await FetchLatestAsync(channel, timeout.Token);
            if (info is null)
                return new(UpdateOutcome.NoRelease, null, null);
            if (!SemanticVersion.TryParse(info.Version, out var latest))
                return new(UpdateOutcome.Failed, null, "The update server gave a version that isn't valid.");
            // A stable client is never offered a beta (the server shouldn't send one, but the client doesn't rely on it).
            if (channel != "beta" && (info.ReleaseChannel == "beta" || latest!.IsPreRelease))
                return new(UpdateOutcome.NoRelease, null, null);
            if (!(latest! > Current))
                return new(UpdateOutcome.UpToDate, info, null);

            bool required = SemanticVersion.TryParse(info.MinimumVersion, out var minimum) && Current < minimum!;
            return new(required ? UpdateOutcome.Required : UpdateOutcome.Available, info, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UriFormatException)
        {
            return new(UpdateOutcome.Failed, null, ex is TaskCanceledException ? "The update server didn't answer in time." : ex.Message);
        }
    }

    /// <summary>The endpoint's answer; null means no published release on the channel or an unknown product (404).</summary>
    private async Task<UpdateInfo?> FetchLatestAsync(string channel, CancellationToken cancel)
    {
        var url = $"{Endpoint.GetLeftPart(UriPartial.Path)}?channel={Uri.EscapeDataString(channel)}&current={Uri.EscapeDataString(Current.ToString())}";
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"The update server answered {(int)response.StatusCode} {response.ReasonPhrase}.");

        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var info = await JsonSerializer.DeserializeAsync<UpdateInfo>(stream, Json, cancel);
        if (info is null || info.Version.Length == 0 || info.DownloadUrl.Length == 0 || info.Size <= 0 || info.Sha256.Length != 64)
            throw new InvalidOperationException("The update server's answer is incomplete.");
        if (!string.Equals(info.Product, ProductSlug, StringComparison.Ordinal))
            throw new InvalidOperationException($"The update server described another product ({info.Product}).");
        if (info.ReleaseChannel.Length == 0)
            info.ReleaseChannel = info.Channel;   // the plugin always sends it; the guide's example once didn't

        if (!IsAllowedDownload(new Uri(info.DownloadUrl)))
            throw new InvalidOperationException("The download address isn't on the update server.");
        return info;
    }

    /// <summary>Only download from the endpoint's own host, over HTTPS (or loopback HTTP for the check programs).</summary>
    public bool IsAllowedDownload(Uri download) =>
        download.IsAbsoluteUri && IsSecure(download) && download.Scheme == Endpoint.Scheme
        && string.Equals(download.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase) && download.Port == Endpoint.Port;

    /// <summary>
    /// Downloads the installer into <paramref name="folder"/> as <c>&lt;fileStem&gt;-&lt;version&gt;.exe</c> (the server's file name
    /// isn't signed, so it isn't used) and verifies it.  The file is deleted when anything fails.
    /// </summary>
    public async Task<string> DownloadAndVerifyAsync(UpdateInfo info, string folder, string fileStem, IProgress<(long Done, long Total)>? progress, CancellationToken cancel)
    {
        if (!SemanticVersion.TryParse(info.Version, out var version))
            throw new InvalidOperationException("The release's version isn't valid.");
        if (!IsAllowedDownload(new Uri(info.DownloadUrl)))
            throw new InvalidOperationException("The download address isn't on the update server.");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{fileStem}-{version}.exe");
        try
        {
            using var response = await _http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The download failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
            if (response.Content.Headers.ContentLength is long length && length != info.Size)
                throw new InvalidOperationException($"The file size is wrong (expected {info.Size:N0} bytes, the server is sending {length:N0}).");
            await using (var source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    done += read;
                    // Never write more than the release's size: a longer file is wrong anyway.
                    if (done > info.Size)
                        throw new InvalidOperationException($"The file size is wrong (more than the expected {info.Size:N0} bytes).");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    progress?.Report((done, info.Size));
                }
            }

            var problem = Verify(path, info);
            if (problem != null)
                throw new InvalidOperationException(problem);
            return path;
        }
        catch
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>The message the publisher signed; must match MFU_Signature::build_message() and Get-MfuMessage.</summary>
    public static string BuildMessage(string slug, string version, string releaseChannel, string sha256, long size)
        => string.Join("\n", MessageHeader, slug, version, releaseChannel, sha256.ToLowerInvariant(), size.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Size, SHA-256 and signature of a downloaded installer.  Null when all match, otherwise the reason.</summary>
    public string? Verify(string filePath, UpdateInfo info)
    {
        var file = new FileInfo(filePath);
        if (!file.Exists)
            return "The downloaded file is missing.";
        if (file.Length != info.Size)
            return $"The file size is wrong (expected {info.Size:N0} bytes, got {file.Length:N0}).";

        string hash;
        using (var stream = file.OpenRead())
            hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(hash, info.Sha256, StringComparison.OrdinalIgnoreCase))
            return "The file's SHA-256 doesn't match the release.";

        if (!PublicKeys.TryGetValue(info.KeyId, out var pem))
            return $"The release is signed with a key this version doesn't know ({info.KeyId}). Download the update from multifarious.filkin.com instead.";

        byte[] signature;
        try { signature = Convert.FromBase64String(Regex.Replace(info.Signature, @"\s+", "")); }
        catch (FormatException) { return "The release's signature isn't valid Base64."; }

        // releaseChannel, not the channel asked for: the publisher signed the release with its own channel.
        var message = Encoding.UTF8.GetBytes(BuildMessage(ProductSlug, info.Version, info.ReleaseChannel, hash, file.Length));
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(pem);
            // Rfc3279DerSequence: the overload without a format expects IEEE P1363 and rejects every signature.
            return ecdsa.VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)
                ? null
                : "The release's signature doesn't verify.";
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            return "The release's signature couldn't be checked: " + ex.Message;
        }
    }
}
