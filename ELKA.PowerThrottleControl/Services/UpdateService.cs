using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ELKA.PowerThrottleControl.Services;

internal sealed record UpdateAsset(string Name, Uri DownloadUrl, long Size);
internal sealed record AvailableUpdate(Version Version, UpdateAsset Package, UpdateAsset Checksums);
internal sealed record DownloadedUpdate(string Path, string Sha256);

internal sealed class UpdateService(HttpClient client)
{
    internal const string Repository = "torment78/ELKA.PowerThrottleControl";
    private const long MaxPackageSize = 500 * 1024 * 1024;

    public static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ELKA.PowerThrottleControl-Updater/1.0");
        return client;
    }

    public async Task<AvailableUpdate?> CheckAsync(Version currentVersion, bool portable, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("GitHub is temporarily limiting update checks. Please try again later.");
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(timeout.Token), currentVersion, portable);
    }

    internal static AvailableUpdate? ParseRelease(string json, Version currentVersion, bool portable)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("The GitHub release has an unsupported version number.");
        var normalizedCurrent = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        if (version <= normalizedCurrent) return null;

        var packageName = portable
            ? $"ELKA_Power_Throttle_Control_Portable_{version}.zip"
            : $"ELKA_Power_Throttle_Control_Setup_{version}.exe";
        UpdateAsset ReadAsset(string name, long maximumSize)
        {
            var matches = release.GetProperty("assets").EnumerateArray()
                .Where(asset => asset.GetProperty("name").GetString() == name).ToArray();
            if (matches.Length != 1 || matches[0].GetProperty("state").GetString() != "uploaded")
                throw new InvalidDataException("The new release is still being prepared. Please check again shortly.");
            var asset = matches[0];
            var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
            if (asset.GetProperty("browser_download_url").GetString() != expectedUrl)
                throw new InvalidDataException("The update download is not from this application's GitHub release.");
            var size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > maximumSize) throw new InvalidDataException("The update has an invalid file size.");
            return new UpdateAsset(name, new Uri(expectedUrl), size);
        }

        return new AvailableUpdate(version, ReadAsset(packageName, MaxPackageSize), ReadAsset("SHA256SUMS.txt", 64 * 1024));
    }

    public async Task<DownloadedUpdate> DownloadAsync(AvailableUpdate update, string destinationRoot,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        using var checksumResponse = await client.GetAsync(update.Checksums.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        await using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var checksumBytes = new MemoryStream();
        await CopyBoundedAsync(checksumStream, checksumBytes, update.Checksums.Size, null, cancellationToken);
        var expectedHash = ParseChecksum(System.Text.Encoding.UTF8.GetString(checksumBytes.ToArray()), update.Package.Name);

        // Each attempt has its own folder; never replace a user's existing download.
        var directory = Path.Combine(destinationRoot, $"v{update.Version}", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, update.Package.Name);
        var partialPath = path + ".partial";
        try
        {
            using var response = await client.GetAsync(update.Package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await CopyBoundedAsync(input, output, update.Package.Size, progress, cancellationToken);
            }
            await using (var file = File.OpenRead(partialPath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded update failed verification. Please download it again.");
            }
            File.Move(partialPath, path);
            return new DownloadedUpdate(path, expectedHash);
        }
        finally
        {
            // Only the partial file created by this attempt is eligible for cleanup.
            try { File.Delete(partialPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static string ParseChecksum(string text, string packageName)
    {
        var hashes = text.Split('\n').Select(line => Regex.Match(line.Trim().TrimStart('\uFEFF'), @"^([a-fA-F0-9]{64})\s+\*?(.+)$"))
            .Where(match => match.Success && match.Groups[2].Value == packageName)
            .Select(match => match.Groups[1].Value).ToArray();
        if (hashes.Length != 1) throw new InvalidDataException("The release does not contain an unambiguous checksum for this download.");
        return hashes[0];
    }

    private static async Task CopyBoundedAsync(Stream input, Stream output, long expectedSize,
        IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        var lastPercent = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += count;
            if (total > expectedSize) throw new InvalidDataException("The download is larger than the published release file.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            var percent = (int)(total * 100 / expectedSize);
            if (percent != lastPercent) progress?.Report(percent);
            lastPercent = percent;
        }
        if (total != expectedSize) throw new InvalidDataException("The download was incomplete. Please try again.");
    }
}
