using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ELKA.PowerThrottleControl.Services;

if (args.Contains("--live"))
{
    using var liveClient = UpdateService.CreateClient();
    var liveService = new UpdateService(liveClient);
    foreach (var portable in new[] { false, true })
    {
        var release = await liveService.CheckAsync(new Version(0, 0, 0), portable, CancellationToken.None);
        Check(release is not null, "Live GitHub check resolves " + (portable ? "portable ZIP" : "installer"));
        Console.WriteLine($"Latest: {release!.Version}; package: {release.Package.Name}");
        if (args.Contains("--download"))
        {
            var root = Directory.CreateTempSubdirectory("ElkaUpdater-LiveCheck-").FullName;
            try
            {
                var download = await liveService.DownloadAsync(release, root, null, CancellationToken.None);
                Check(File.Exists(download.Path), "Live release download passes SHA-256 verification (not executed)");
            }
            finally { Directory.Delete(root, true); }
        }
    }
    return;
}

var package = Encoding.UTF8.GetBytes("Non-executable updater test fixture");
var hash = Convert.ToHexString(SHA256.HashData(package));
const string installerName = "ELKA_Power_Throttle_Control_Setup_1.3.4.exe";
const string zipName = "ELKA_Power_Throttle_Control_Portable_1.3.4.zip";
var checksums = $"{hash}  {installerName}\r\n{hash}  {zipName}\r\n";
var json = Release("v1.3.4", false, false, new[] { installerName, zipName, "SHA256SUMS.txt" });

Check(UpdateService.ParseRelease(json, new Version(1, 3, 4, 0), false) is null, "An identical version is not offered again");
Check(UpdateService.ParseRelease(json, new Version(2, 0, 0), false) is null, "Updates never downgrade");
Check(UpdateService.ParseRelease(Release("v1.3.4", true, false, []), new Version(1, 3, 3), false) is null, "Draft releases are ignored");
Check(UpdateService.ParseRelease(Release("v1.3.4", false, true, []), new Version(1, 3, 3), false) is null, "Prereleases are ignored");
Check(UpdateService.ParseRelease(json, new Version(1, 3, 3, 0), false)!.Package.Name == installerName, "Installed copies select the installer");
Check(UpdateService.ParseRelease(json, new Version(1, 3, 3), true)!.Package.Name == zipName, "Portable copies select only the ZIP");
Expect<InvalidDataException>(() => UpdateService.ParseRelease(Release("v1.3.4", false, false, [installerName, "SHA256SUMS.txt"]), new Version(1, 3, 3), true), "A missing ZIP never falls back to an installer");
Expect<InvalidDataException>(() => UpdateService.ParseRelease(json.Replace("https://github.com/", "https://example.com/"), new Version(1, 3, 3), false), "Assets from another origin are refused");
Expect<InvalidDataException>(() => UpdateService.ParseRelease(Release("v1.3.4", false, false, [zipName]), new Version(1, 3, 3), true), "A release without checksums cannot be offered");
Expect<InvalidDataException>(() => UpdateService.ParseChecksum(checksums + checksums, zipName), "Ambiguous checksums are refused");
Check(UpdateService.ParseChecksum(checksums, zipName) == hash, "The ZIP checksum is selected by exact filename");

Check(UpdateLauncher.MatchesInstalledPath(@"C:\Program Files\ElkaSoft\ELKA Power Throttle Control\ELKA.PowerThrottleControl.exe", @"C:\Program Files\ElkaSoft\ELKA Power Throttle Control\"), "A registered install is recognized");
Check(!UpdateLauncher.MatchesInstalledPath(@"E:\USB\ELKA.PowerThrottleControl.exe", @"C:\Program Files\ElkaSoft\ELKA Power Throttle Control"), "A USB copy stays portable even if the app is also installed");
Check(!UpdateLauncher.MatchesInstalledPath(@"E:\USB\ELKA.PowerThrottleControl.exe", null), "An unregistered copy is portable");
Check(!UpdateLauncher.MatchesInstalledPath(@"E:\USB\ELKA.PowerThrottleControl.exe", "\0"), "An invalid registration cannot crash portable detection");

var testRoot = Directory.CreateTempSubdirectory("ElkaUpdater-Checks-").FullName;
try
{
    var servedPackage = package;
    using var client = new HttpClient(new StubHandler(request =>
    {
        if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest")) return new(HttpStatusCode.OK) { Content = new StringContent(json) };
        if (request.RequestUri.AbsolutePath.EndsWith("SHA256SUMS.txt")) return new(HttpStatusCode.OK) { Content = new StringContent(checksums) };
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(servedPackage) };
    }));
    var service = new UpdateService(client);
    var update = (await service.CheckAsync(new Version(1, 3, 3), true, CancellationToken.None))!;
    var downloaded = await service.DownloadAsync(update, Path.Combine(testRoot, "usb"), null, CancellationToken.None);
    Check(File.ReadAllBytes(downloaded.Path).SequenceEqual(package) && downloaded.Path.EndsWith(".zip"), "A verified ZIP is saved in the requested portable folder");
    servedPackage = package.Select(b => (byte)(b ^ 1)).ToArray();
    await ExpectAsync<InvalidDataException>(() => service.DownloadAsync(update, Path.Combine(testRoot, "bad-hash"), null, CancellationToken.None), "Corrupted content is refused before handoff");
    servedPackage = package[..^1];
    await ExpectAsync<InvalidDataException>(() => service.DownloadAsync(update, Path.Combine(testRoot, "truncated"), null, CancellationToken.None), "Truncated downloads are refused");
    Check(!Directory.EnumerateFiles(testRoot, "*.partial", SearchOption.AllDirectories).Any(), "Failed attempts remove only their partial files");
    Check(Directory.EnumerateFiles(testRoot, "*.zip", SearchOption.AllDirectories).Count() == 1, "Failed attempts never produce usable update packages");
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    await ExpectAsync<OperationCanceledException>(() => service.DownloadAsync(update, testRoot, null, cancelled.Token), "Cancellation stops the download");
    using var limitedClient = new HttpClient(new StubHandler(_ => new(HttpStatusCode.Forbidden)));
    await ExpectAsync<InvalidOperationException>(() => new UpdateService(limitedClient).CheckAsync(new Version(1, 3, 3), true, CancellationToken.None), "GitHub rate limits produce a recoverable error");
}
finally { Directory.Delete(testRoot, true); }
Console.WriteLine("All updater checks passed.");

string Release(string tag, bool draft, bool prerelease, string[] names) => JsonSerializer.Serialize(new
{
    tag_name = tag, draft, prerelease,
    assets = names.Select(name => new
    {
        name, state = "uploaded",
        browser_download_url = $"https://github.com/{UpdateService.Repository}/releases/download/{tag}/{name}",
        size = name == "SHA256SUMS.txt" ? Encoding.UTF8.GetByteCount(checksums) : package.Length
    })
});
static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    Console.WriteLine("PASS: " + description);
}
static void Expect<T>(Action action, string description) where T : Exception
{
    try { action(); } catch (T) { Console.WriteLine("PASS: " + description); return; }
    throw new InvalidOperationException(description);
}
static async Task ExpectAsync<T>(Func<Task> action, string description) where T : Exception
{
    try { await action(); } catch (T) { Console.WriteLine("PASS: " + description); return; }
    throw new InvalidOperationException(description);
}
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(respond(request));
    }
}
