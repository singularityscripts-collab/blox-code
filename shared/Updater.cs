using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BloxCode.Core;

/// <summary>A newer release: where to download this platform's file (tried in order) and what it must hash to.</summary>
public sealed record UpdateInfo(Version Version, string Tag, IReadOnlyList<string> DownloadUrls, long Size, string Sha256, string? Title);

/// <summary>
/// Keeps Blox Code itself current, following the flow recovered from Bloxstrap: find the latest
/// release, download its exe into Temp\Updates, start it with -upgrade, and let that new process
/// copy itself over the installed exe once the old one has exited.
///
/// Releases are described by a signed manifest (latest.json + latest.json.sig, written by
/// tools/release.js) that can sit on any static host, so no single website decides whether
/// Blox Code can update. Every feed is tried in order; a manifest only counts if it was signed with
/// the key built into the app, and the file it points to only runs if its SHA-256 matches.
/// </summary>
public static class Updater
{
    private const string MutexName = "BloxCode-AutoUpdater";
    public const string ManifestName = "latest.json";
    public const string SignatureName = "latest.json.sig";
    public const string Product = "BloxCode";

    private static string? Metadata(string key) => typeof(Updater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } value ? value.Trim() : null;

    /// <summary>Feed folders baked in at build time (UpdateFeeds in Directory.Build.props).</summary>
    private static IReadOnlyList<string> BuiltInFeeds { get; } = SplitFeeds(Metadata("UpdateFeeds"));

    /// <summary>The feeds to try; BLOXCODE_UPDATE_FEED (same ';' list) replaces them for testing against a local server.</summary>
    public static IReadOnlyList<string> Feeds =>
        Environment.GetEnvironmentVariable("BLOXCODE_UPDATE_FEED") is { Length: > 0 } feeds ? SplitFeeds(feeds) : BuiltInFeeds;

    private static string[] SplitFeeds(string? list) =>
        (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The release-signing public key (base64 SubjectPublicKeyInfo, ECDSA P-256).</summary>
    public static string? PublicKey { get; } = Metadata("UpdatePublicKey");

    /// <summary>Where the "Source code" buttons go, or null to hide them.</summary>
    public static string? Homepage { get; } = Metadata("HomepageUrl");

    /// <summary>The Discord application for Rich Presence (DiscordAppId in Directory.Build.props; BLOXCODE_DISCORD_APP overrides it for testing).</summary>
    public static string? DiscordAppId { get; } =
        Environment.GetEnvironmentVariable("BLOXCODE_DISCORD_APP") is { Length: > 0 } test ? test.Trim() : Metadata("DiscordAppId");

    public static bool IsConfigured => Feeds.Count > 0 && PublicKey is not null;

    /// <summary>The host of the first feed, for "updates come from …" lines.</summary>
    public static string? FeedHost => Feeds.Count > 0 && Uri.TryCreate(FeedFolder(Feeds[0]), UriKind.Absolute, out var uri) ? uri.Host : null;

    public static Version Current { get; } = Trim(typeof(Updater).Assembly.GetName().Version ?? new Version(1, 0, 0));

    /// <summary>The manifest key for the file this platform runs.</summary>
    public static string PlatformKey => OperatingSystem.IsWindows() ? "windows-x64" : "linux-x64";

    // Linux keeps downloads out of /tmp, which some systems mount noexec (the update has to run from there).
    public static string UpdatesFolder => Path.Combine(Paths.Cache, Paths.IsXdg ? "updates" : "Updates");

    /// <summary>The release asset this platform runs: the WPF exe on Windows, the AppImage on Linux.</summary>
    public static string AssetName => OperatingSystem.IsWindows() ? "BloxCode.exe" : "BloxCode-x86_64.AppImage";

    private static string AssetExtension => Path.GetExtension(AssetName);

    /// <summary>Platform work after the installed copy was replaced (Windows: the Apps &amp; Features entry).</summary>
    public static Action? AfterUpgrade { get; set; }

    /// <summary>
    /// Returns the newer release, or null when already current. Feeds are tried in order and the
    /// first one with a correctly signed manifest decides; if none works, the last error is thrown.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, CancellationToken ct)
    {
        const string ident = "Updater::Check";
        if (!IsConfigured)
            return null;

        Exception? last = null;
        foreach (string feed in Feeds)
        {
            try
            {
                string folder = FeedFolder(feed);
                byte[] manifest = await GetBytesAsync(http, folder + ManifestName, 1 << 20, ct);
                byte[] signature = await GetBytesAsync(http, folder + SignatureName, 4096, ct);
                var info = ReadManifest(manifest, signature, PublicKey!, new Uri(folder + ManifestName), PlatformKey);

                if (info is null)
                {
                    Logger.Warn(ident, $"{folder} has no {PlatformKey} build in its release");
                    return null;
                }
                if (info.Version <= Current)
                {
                    Logger.Info(ident, $"Up to date ({Current}, latest {info.Version} from {folder})");
                    return null;
                }
                Logger.Info(ident, $"Update available: {Current} -> {info.Version} (from {folder})");
                return info;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Logger.Warn(ident, $"Feed {feed} failed: {ex.Message}");
                last = ex;
            }
        }
        throw last ?? new InvalidOperationException("No update feed answered.");
    }

    /// <summary>Feed URLs are folders; accept them with or without the trailing slash or the file name.</summary>
    public static string FeedFolder(string feed)
    {
        feed = feed.Trim();
        if (feed.EndsWith("/" + ManifestName, StringComparison.OrdinalIgnoreCase))
            feed = feed[..^ManifestName.Length];
        return feed.EndsWith('/') ? feed : feed + "/";
    }

    private static async Task<byte[]> GetBytesAsync(HttpClient http, string url, int limit, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // Static hosts and CDNs may hold on to the previous manifest; ask for a fresh copy.
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException($"{url} is too large to be an update manifest.");
        byte[] data = await response.Content.ReadAsByteArrayAsync(ct);
        if (data.Length > limit)
            throw new InvalidDataException($"{url} is too large to be an update manifest.");
        return data;
    }

    /// <summary>
    /// Verifies a manifest against its signature and reads this platform's file from it; null when
    /// the release has no file for the platform. Throws when the signature or contents are wrong.
    /// </summary>
    public static UpdateInfo? ReadManifest(byte[] manifest, byte[] signatureFile, string publicKey, Uri manifestUrl, string platform)
    {
        if (!VerifySignature(manifest, signatureFile, publicKey))
            throw new InvalidDataException("The update manifest isn't signed with Blox Code's release key.");

        using var doc = JsonDocument.Parse(manifest);
        var root = doc.RootElement;
        if (root.GetProperty("format").GetInt32() != 1)
            throw new InvalidDataException("The update manifest uses a newer format; download the latest Blox Code by hand.");
        if (root.GetProperty("product").GetString() != Product)
            throw new InvalidDataException("The update manifest is for a different program.");

        string versionText = root.GetProperty("version").GetString() ?? "";
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidDataException($"The update manifest has an unreadable version '{versionText}'.");
        version = Trim(version);

        if (!root.GetProperty("assets").TryGetProperty(platform, out var asset))
            return null;

        string sha256 = (asset.GetProperty("sha256").GetString() ?? "").ToLowerInvariant();
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update manifest has a malformed checksum.");

        var urls = new List<string>();
        foreach (var url in asset.GetProperty("urls").EnumerateArray())
        {
            // Relative paths point next to the manifest, so one folder works on any host.
            if (url.GetString() is { Length: > 0 } text && Uri.TryCreate(manifestUrl, text, out var absolute)
                && (absolute.Scheme == Uri.UriSchemeHttps || absolute.IsLoopback))
                urls.Add(absolute.AbsoluteUri);
        }
        if (urls.Count == 0)
            throw new InvalidDataException("The update manifest lists no download address.");

        return new UpdateInfo(version, "v" + version, urls,
            asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
            sha256,
            root.TryGetProperty("title", out var title) ? title.GetString() : null);
    }

    /// <summary>
    /// Checks latest.json.sig: base64 of an ECDSA P-256 / SHA-256 signature (r‖s) over the exact
    /// bytes of latest.json.
    /// </summary>
    public static bool VerifySignature(byte[] data, byte[] signatureFile, string publicKey)
    {
        try
        {
            byte[] signature = Convert.FromBase64String(System.Text.Encoding.ASCII.GetString(signatureFile).Trim());
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(UpdatesFolder);
        string path = Path.Combine(UpdatesFolder, $"BloxCode-{info.Version}{AssetExtension}");

        Exception? last = null;
        foreach (string url in info.DownloadUrls)
        {
            try
            {
                await DownloadFileAsync(http, url, path, info, progress, ct);
                Paths.MakeExecutable(path);
                Logger.Info("Updater::Download", $"Downloaded {info.Tag} from {url} to {path}");
                return path;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Logger.Warn("Updater::Download", $"{url} failed: {ex.Message}");
                last = ex;
                try { File.Delete(path); } catch { }
            }
        }
        throw last!;
    }

    private static async Task DownloadFileAsync(HttpClient http, string url, string path, UpdateInfo info, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        // Same idle watchdog as package downloads: HttpClient.Timeout doesn't cover the body.
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(TimeSpan.FromSeconds(30));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long read = 0;
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token))
        {
            response.EnsureSuccessStatusCode();
            long total = info.Size > 0 ? info.Size : response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
            await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);

            var buffer = new byte[1 << 16];
            long reported = 0;
            int n;
            while ((n = await source.ReadAsync(buffer, stall.Token)) > 0)
            {
                stall.CancelAfter(TimeSpan.FromSeconds(30));
                read += n;
                if (info.Size > 0 && read > info.Size)
                    throw new InvalidDataException("The update download is larger than the release says.");
                hash.AppendData(buffer, 0, n);
                await file.WriteAsync(buffer.AsMemory(0, n), ct);
                if (read - reported >= 1 << 18)
                {
                    progress.Report((read, total));
                    reported = read;
                }
            }
            progress.Report((read, total));
        }

        if (info.Size > 0 && read != info.Size)
            throw new InvalidDataException("The update download was cut short.");

        string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        if (actual != info.Sha256)
            throw new InvalidDataException($"The downloaded update failed its checksum (expected {info.Sha256}, got {actual}).");
    }

    /// <summary>Starts the downloaded exe in upgrade mode, forwarding the original launch arguments.</summary>
    public static void LaunchUpgrade(string exe, IEnumerable<string> forwardArgs)
    {
        var psi = StartInfo(exe);
        psi.ArgumentList.Add("-upgrade");
        foreach (string arg in forwardArgs)
            psi.ArgumentList.Add(arg);
        Process.Start(psi)?.Dispose();
    }

    /// <summary>
    /// How to start a copy of Blox Code. An AppImage that had to be unpacked (no FUSE) passes that
    /// on, otherwise the next copy would fail to open.
    /// </summary>
    public static ProcessStartInfo StartInfo(string exe)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        if (Paths.IsExtractedAppImage)
            psi.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
        return psi;
    }

    /// <summary>
    /// Runs inside the freshly downloaded exe: replace the installed copy, refresh the install
    /// metadata, then hand over to the installed exe. Returns false if the copy couldn't be made.
    /// </summary>
    public static bool ApplyUpgrade(IEnumerable<string> forwardArgs)
    {
        const string ident = "Updater::ApplyUpgrade";
        using var mutex = new Mutex(false, MutexName);
        bool owned = false;

        try
        {
            try { owned = mutex.WaitOne(TimeSpan.FromSeconds(10)); }
            catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                Logger.Warn(ident, "Failed to update! (Could not obtain the updater mutex)");
                return false;
            }

            // The old process may still be closing; wait for it to let go of the file.
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    Paths.InstallExecutable(Paths.Process, Paths.Application);
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 10)
                {
                    Logger.Info(ident, "Waiting for write permissions to update");
                    Thread.Sleep(500);
                }
            }

            // The new copy is in place; a shortcut or registry hiccup must not turn that into a failure.
            try { AfterUpgrade?.Invoke(); }
            catch (Exception ex) { Logger.Error(ident, ex); }

            Store.State.JustUpdatedTo = Current.ToString();
            Store.SaveState();
            Logger.Info(ident, $"Upgraded installed copy to {Current}");
        }
        catch (Exception ex)
        {
            Logger.Error(ident, ex);
            return false;
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }

        RunInstalled(forwardArgs);
        return true;
    }

    /// <summary>Starts the installed copy with the original arguments, skipping its update check.</summary>
    public static bool RunInstalled(IEnumerable<string> forwardArgs) => RunCopy(Paths.Application, forwardArgs);

    /// <summary>Starts a copy of Blox Code with -noupdate and the given arguments; false (logged) if it can't.</summary>
    public static bool RunCopy(string exe, IEnumerable<string> forwardArgs)
    {
        if (!File.Exists(exe))
            return false;
        try
        {
            var psi = StartInfo(exe);
            psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
            psi.ArgumentList.Add("-noupdate");
            foreach (string arg in forwardArgs)
                psi.ArgumentList.Add(arg);
            Process.Start(psi)?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Logger.Error("Updater::RunCopy", ex);
            return false;
        }
    }

    /// <summary>Deletes leftover downloads once we're no longer running from that folder.</summary>
    public static void CleanupUpdatesFolder()
    {
        if (!Directory.Exists(UpdatesFolder) || Paths.Process.StartsWith(UpdatesFolder, Paths.PathComparison))
            return;
        foreach (string file in Directory.EnumerateFiles(UpdatesFolder))
        {
            try { File.Delete(file); } catch { }
        }
    }

    private static Version Trim(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
