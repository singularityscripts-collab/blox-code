using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace BloxCode.Core;

/// <summary>
/// Keeps the Roblox player up to date and launches it. The flow mirrors what was recovered from
/// Bloxstrap.Bootstrapper: pick a mirror, resolve the channel's latest version, download packages
/// into an MD5-keyed cache, extract them through the package map, apply mods + FastFlags, start.
/// </summary>
public sealed partial class Bootstrapper(HttpClient http, LaunchSettings launch, IProgress<BootstrapProgress> progress)
{
    private const string PlayerExecutable = "RobloxPlayerBeta.exe";
    private const string StudioExecutable = "RobloxStudioBeta.exe";

    /// <summary>Roblox Studio rather than the player: its own version, packages and executable.</summary>
    private bool Studio => launch.Mode == LaunchMode.Studio;
    private string Executable => Studio ? StudioExecutable : PlayerExecutable;
    private string AppName => Studio ? "Roblox Studio" : "Roblox";
    private IReadOnlyDictionary<string, string> DirectoryMap => Studio ? PackageManifest.StudioDirectoryMap : PackageManifest.PlayerDirectoryMap;
    private const string WebView2ClientKey = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
    private const string OriginalsFolder = ".bloxcode-originals";

    // A download that delivers nothing for this long is treated as dead and retried.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    // One stream already fills a typical home connection; three keep it busy while others hash or unpack.
    private const int ParallelDownloads = 3;

    private const string AppSettingsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n" +
        "<Settings>\r\n" +
        "\t<ContentFolder>content</ContentFolder>\r\n" +
        "\t<BaseUrl>http://www.roblox.com</BaseUrl>\r\n" +
        "</Settings>\r\n";

    [GeneratedRegex("channel:([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelArgRegex();

    [GeneratedRegex("^[a-z0-9_-]{1,64}$")]
    private static partial Regex ValidChannelRegex();

    public string? VersionGuid { get; private set; }

    /// <summary>When Roblox was started (UTC); null when nothing was launched.</summary>
    public DateTime? LaunchedAt { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        const string ident = "Bootstrapper::Run";
        Logger.Info(ident, $"Starting (args: '{launch.RobloxArgs}')");

        Report(BootstrapPhase.Connecting, "Connecting to Roblox");
        await Deployment.InitializeConnectivity(http, ct);

        Report(BootstrapPhase.Checking, "Checking for updates");
        var (channel, info) = await ResolveVersion(ct);
        VersionGuid = info.VersionGuid;

        var state = Store.State;
        string versionDir = Path.Combine(Paths.Versions, info.VersionGuid);
        string? installed = Studio ? state.StudioVersionGuid : state.VersionGuid;
        bool upToDate = installed == info.VersionGuid
                        && File.Exists(Path.Combine(versionDir, Executable))
                        && !(state.ForceReinstall && !Studio) && !launch.Force;

        if (!upToDate)
        {
            await InstallVersion(info.VersionGuid, versionDir, ct);
            if (Studio)
            {
                state.StudioVersionGuid = info.VersionGuid;
            }
            else
            {
                state.VersionGuid = info.VersionGuid;
                state.Channel = channel;
                state.ForceReinstall = false;
            }
            Store.SaveState();
        }

        Report(BootstrapPhase.Installing, "Applying your tweaks");
        // Mods replace player files (cursor, sounds, font); Studio gets the Fast Flags only.
        if (!Studio)
            ApplyModifications(versionDir);
        WriteFastFlags(versionDir);
        if (!Studio)
            ResetQualityCacheIfAsked();
        Store.SaveState();

        if (!upToDate && Store.Settings.CleanupOldVersions)
            CleanupOldVersions(state.VersionGuid, state.StudioVersionGuid, Store.Settings.PinnedVersion);

        Logger.Info(ident, $"{info.VersionGuid} ready ({(upToDate ? "already up to date" : "freshly installed")})");

        if (launch.NoLaunch)
        {
            Report(BootstrapPhase.Done, $"{AppName} is up to date", 1);
            return;
        }

        Report(BootstrapPhase.Launching, $"Starting {AppName}", 1);
        // Installing or updating Roblox can hand roblox:// links back to Roblox itself; take them back.
        Installer.ReclaimLinksFromRoblox();
        // Before the player starts: it must find the singleton name already taken.
        if (Store.Settings.MultiInstance && !Studio)
            MultiInstance.Acquire();
        StartRoblox(versionDir);
        Report(BootstrapPhase.Done, $"{AppName} is running", 1);
    }

    private async Task<(string Channel, ClientVersion Info)> ResolveVersion(CancellationToken ct)
    {
        string channel = (launch.Channel ?? ChannelFromArgs() ?? Store.Settings.Channel ?? "").Trim().ToLowerInvariant();
        // The channel ends up in a URL path; anything but a plain name (spaces, slashes) is a typo.
        if (!ValidChannelRegex().IsMatch(channel))
        {
            if (channel.Length > 0)
                Logger.Warn("Bootstrapper::ResolveVersion", $"Ignoring invalid channel '{channel}'");
            channel = Deployment.DefaultChannel;
        }

        if (Studio)
        {
            try
            {
                return (channel, await Deployment.GetInfo(http, channel, ct, Deployment.StudioBinaryType));
            }
            catch (InvalidChannelException) when (channel != Deployment.DefaultChannel)
            {
                return (Deployment.DefaultChannel, await Deployment.GetInfo(http, Deployment.DefaultChannel, ct, Deployment.StudioBinaryType));
            }
        }

        // A pinned version skips the channel lookup: the packages are fetched by their version-… name.
        if (Store.Settings.PinnedVersion is { } pinned)
        {
            if (PinnedVersionRegex().IsMatch(pinned))
            {
                Logger.Info("Bootstrapper::ResolveVersion", $"Staying on pinned {pinned}");
                return (channel, new ClientVersion { VersionGuid = pinned, Version = pinned });
            }
            Logger.Warn("Bootstrapper::ResolveVersion", $"Ignoring invalid pinned version '{pinned}'");
        }

        try
        {
            return (channel, await Deployment.GetInfo(http, channel, ct));
        }
        catch (InvalidChannelException ex) when (channel != Deployment.DefaultChannel)
        {
            Logger.Warn("Bootstrapper::ResolveVersion", $"{ex.Message} - resetting channel to {Deployment.DefaultChannel}");
            return (Deployment.DefaultChannel, await Deployment.GetInfo(http, Deployment.DefaultChannel, ct));
        }
    }

    [GeneratedRegex(@"^version-[0-9a-f]{16}$")]
    public static partial Regex PinnedVersionRegex();

    private string? ChannelFromArgs()
    {
        var match = ChannelArgRegex().Match(launch.RobloxArgs);
        return match.Success && match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : null;
    }

    private async Task InstallVersion(string versionGuid, string versionDir, CancellationToken ct)
    {
        const string ident = "Bootstrapper::InstallVersion";
        Report(BootstrapPhase.Checking, "Reading package manifest");

        string manifestText;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            manifestText = await http.GetStringAsync(Deployment.GetLocation($"/{versionGuid}-rbxPkgManifest.txt"), timeout.Token);
        }
        var packages = PackageManifest.Parse(manifestText)
            .Where(p => DirectoryMap.ContainsKey(p.Name))
            .ToList();
        string main = Studio ? "RobloxStudio.zip" : "RobloxApp.zip";
        if (!packages.Any(p => p.Name == main))
            throw new InvalidDataException($"The package manifest for {versionGuid} has no {main}");
        Logger.Info(ident, $"{packages.Count} packages for {versionGuid}");

        if (Directory.Exists(versionDir))
        {
            // Wiping a folder Roblox is running from fails halfway and leaves a broken install.
            if (IsRunningFrom(versionDir, Executable))
                throw new IOException($"{AppName} is still open from this version. Close it and try again.");
            Directory.Delete(versionDir, recursive: true);
        }
        Directory.CreateDirectory(versionDir);

        // A few packages download at once, biggest first, and each is unpacked as soon as it lands,
        // so checksums and unpacking overlap with the downloads still running.
        long total = Math.Max(1, packages.Sum(p => p.PackedSize));
        Directory.CreateDirectory(Paths.Downloads);
        var received = new long[packages.Count];
        long reported = 0;
        var progressLock = new object();
        using var downloads = new SemaphoreSlim(ParallelDownloads);
        using var unpacking = new SemaphoreSlim(1);
        using var failed = CancellationTokenSource.CreateLinkedTokenSource(ct);

        void Progress(int index, long bytes, string status)
        {
            Interlocked.Exchange(ref received[index], bytes);
            long sum = received.Sum();
            lock (progressLock)
            {
                // Downloads finish out of order; never let the bar step back.
                if (sum < reported)
                    return;
                reported = sum;
            }
            Report(BootstrapPhase.Downloading, status, (double)sum / total, $"{Mb(sum)} / {Mb(total)}");
        }

        // Two packages with the same MD5 share one cache file: download it once, the other waits.
        var fetches = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);
        var fetchLock = new object();

        async Task Fetch(int index)
        {
            var package = packages[index];
            await downloads.WaitAsync(failed.Token);
            try
            {
                await DownloadPackage(versionGuid, package, bytes => Progress(index, bytes, $"Downloading {package.Name}"), failed.Token);
            }
            finally
            {
                downloads.Release();
            }
        }

        async Task Install(int index)
        {
            var package = packages[index];
            Task fetch;
            lock (fetchLock)
            {
                if (!fetches.TryGetValue(package.Signature, out fetch!))
                    fetches[package.Signature] = fetch = Fetch(index);
            }
            await fetch;
            Progress(index, package.PackedSize, $"Downloading {package.Name}");

            await unpacking.WaitAsync(failed.Token);
            try
            {
                Progress(index, package.PackedSize, $"Unpacking {package.Name}");
                await Task.Run(() => ExtractPackage(package, versionDir, DirectoryMap), failed.Token);
            }
            finally
            {
                unpacking.Release();
            }
        }

        var work = Enumerable.Range(0, packages.Count)
            .OrderByDescending(i => packages[i].PackedSize)
            // Task.Run: checking a cached package's MD5 is synchronous and shouldn't hold up the others.
            .Select(i => Task.Run(async () =>
            {
                try { await Install(i); }
                catch when (!failed.IsCancellationRequested) { failed.Cancel(); throw; }
            }))
            .ToList();
        try
        {
            await Task.WhenAll(work);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Another package failed and cancelled this one; surface that failure, not the cancellation.
            throw work.Select(t => t.Exception?.InnerException).FirstOrDefault(e => e is not null and not OperationCanceledException)
                  ?? new IOException("Installing Roblox failed");
        }

        File.WriteAllText(Path.Combine(versionDir, "AppSettings.xml"), AppSettingsXml);
        var hashes = packages.ToDictionary(p => p.Name, p => p.Signature);
        if (Studio)
        {
            Store.State.StudioPackageHashes = hashes;
        }
        else
        {
            Store.State.PackageHashes = hashes;
            Store.State.ModManifest.Clear();
        }

        await EnsureWebView2(versionDir, ct);
        // Keep the other app's cached packages: they're what a repair would need.
        CleanupDownloadCache(Store.State.PackageHashes.Values.Concat(Store.State.StudioPackageHashes.Values));
    }

    private async Task DownloadPackage(string versionGuid, Package package, Action<long> onBytes, CancellationToken ct)
    {
        const string ident = "Bootstrapper::DownloadPackage";

        if (File.Exists(package.DownloadPath) && Md5Of(package.DownloadPath) == package.Signature)
        {
            Logger.Info(ident, $"{package.Name} found in cache");
            onBytes(package.PackedSize);
            return;
        }

        // Bloxstrap keys its cache by the same MD5, so a verified copy from it saves the download.
        string bloxstrapCopy = Path.Combine(Paths.LocalAppData, "Bloxstrap", "Downloads", package.Signature);
        if (File.Exists(bloxstrapCopy) && Md5Of(bloxstrapCopy) == package.Signature)
        {
            File.Copy(bloxstrapCopy, package.DownloadPath, overwrite: true);
            Logger.Info(ident, $"{package.Name} reused from Bloxstrap's cache");
            onBytes(package.PackedSize);
            return;
        }

        string url = Deployment.GetLocation($"/{versionGuid}-{package.Name}");
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // HttpClient.Timeout stops counting once headers arrive, so a body that stalls would
                // hang forever; this token is pushed forward on every chunk and fires on silence.
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
                stall.CancelAfter(StallTimeout);

                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, stall.Token))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(stall.Token);
                    await using var file = new FileStream(package.DownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);

                    var buffer = new byte[1 << 16];
                    long read = 0, reported = 0;
                    int n;
                    while ((n = await source.ReadAsync(buffer, stall.Token)) > 0)
                    {
                        stall.CancelAfter(StallTimeout);
                        await file.WriteAsync(buffer.AsMemory(0, n), ct);
                        read += n;
                        // Every chunk would flood the dispatcher; a quarter megabyte is smooth enough.
                        if (read - reported >= 1 << 18)
                        {
                            onBytes(read);
                            reported = read;
                        }
                    }
                    onBytes(read);
                }

                string hash = Md5Of(package.DownloadPath);
                if (hash != package.Signature)
                    throw new InvalidDataException($"Checksum mismatch for {package.Name}: expected {package.Signature}, got {hash}");

                Logger.Info(ident, $"Downloaded {package.Name}");
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                TryDelete(package.DownloadPath);
                string reason = ex is OperationCanceledException ? $"no data for {StallTimeout.TotalSeconds:0}s" : ex.Message;
                if (attempt >= 3)
                    throw new HttpRequestException($"Couldn't download {package.Name} after 3 attempts ({reason})", ex);

                Logger.Warn(ident, $"Attempt {attempt} for {package.Name} failed: {reason}");
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
        }
    }

    private static void ExtractPackage(Package package, string versionDir, IReadOnlyDictionary<string, string> map)
    {
        // Trailing separator so "...\version-ab" can't be matched by a sibling like "...\version-abc".
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(Path.Combine(versionDir, map[package.Name]))) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(package.DownloadPath);

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                continue;

            string dest = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', '\\')));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Refusing to extract '{entry.FullName}' outside the version folder");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
    }

    private async Task EnsureWebView2(string versionDir, CancellationToken ct)
    {
        const string ident = "Bootstrapper::EnsureWebView2";
        if (IsWebView2Installed())
            return;

        string installer = Path.Combine(versionDir, "WebView2RuntimeInstaller", "MicrosoftEdgeWebview2Setup.exe");
        if (!File.Exists(installer))
        {
            Logger.Warn(ident, "WebView2 runtime missing and this Roblox version ships no installer");
            return;
        }

        Report(BootstrapPhase.Installing, "Installing WebView2 runtime");
        using var process = Process.Start(new ProcessStartInfo(installer, "/silent /install") { UseShellExecute = false });
        if (process is null)
            return;

        // A stuck installer must not hold the launch hostage; Roblox can still start without it.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Logger.Warn(ident, "WebView2 installer didn't finish within 3 minutes; continuing without waiting");
        }
    }

    private static bool IsWebView2Installed()
    {
        static bool Has(RegistryKey hive, string key)
        {
            using var k = hive.OpenSubKey(key);
            return k?.GetValue("pv") is string pv && pv.Length > 0 && pv != "0.0.0.0";
        }

        return Has(Registry.LocalMachine, WebView2ClientKey.Replace(@"SOFTWARE\", @"SOFTWARE\WOW6432Node\"))
               || Has(Registry.LocalMachine, WebView2ClientKey)
               || Has(Registry.CurrentUser, WebView2ClientKey);
    }

    /// <summary>
    /// Copies everything in the Modifications folder over the version folder. Files that get
    /// overwritten are backed up so removing a mod restores the original.
    /// </summary>
    private static void ApplyModifications(string versionDir)
    {
        const string ident = "Bootstrapper::ApplyModifications";
        var state = Store.State;
        string originals = Path.Combine(versionDir, OriginalsFolder);

        RefreshCustomFont(versionDir, originals);

        // A subfolder that can't be read is skipped rather than stopping Roblox from launching.
        var mods = Directory.Exists(Paths.Modifications)
            ? Directory.EnumerateFiles(Paths.Modifications, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Select(f => Path.GetRelativePath(Paths.Modifications, f))
                .Where(r => !r.Equals("README.txt", StringComparison.OrdinalIgnoreCase))
                .ToList()
            : new List<string>();

        // A mod that can't be written (say, Roblox has the file open) is skipped and retried next
        // launch instead of failing the whole launch. The manifest only records what really happened.
        var applied = new List<string>();

        foreach (string rel in state.ModManifest.Except(mods, StringComparer.OrdinalIgnoreCase).ToList())
        {
            string target = Path.Combine(versionDir, rel), backup = Path.Combine(originals, rel);
            try
            {
                if (File.Exists(backup))
                {
                    File.Copy(backup, target, overwrite: true);
                    File.Delete(backup);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
                Logger.Info(ident, $"Removed mod {rel}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(ident, $"Couldn't remove mod {rel}, will retry: {ex.Message}");
                applied.Add(rel);
            }
        }

        foreach (string rel in mods)
        {
            string source = Path.Combine(Paths.Modifications, rel);
            string target = Path.Combine(versionDir, rel), backup = Path.Combine(originals, rel);
            try
            {
                if (File.Exists(target) && !File.Exists(backup) && !state.ModManifest.Contains(rel, StringComparer.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(target, backup);
                }

                if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length || Md5Of(target) != Md5Of(source))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: true);
                    Logger.Info(ident, $"Applied mod {rel}");
                }
                applied.Add(rel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(ident, $"Couldn't apply mod {rel}, will retry: {ex.Message}");
                // Keep it tracked if an earlier launch did apply it, so removal still restores the original.
                if (state.ModManifest.Contains(rel, StringComparer.OrdinalIgnoreCase))
                    applied.Add(rel);
            }
        }

        state.ModManifest = applied;
    }

    /// <summary>
    /// Points every font family at the custom game font (or drops those rewrites when it's gone),
    /// reading Roblox's own family files: the backed-up original when a mod already replaced one.
    /// </summary>
    private static void RefreshCustomFont(string versionDir, string originals)
    {
        try
        {
            // No custom font: only the rewritten families (if any) need removing, nothing to read.
            if (ModCatalog.CurrentFile(ModCatalog.Font) is null)
            {
                ModCatalog.RebuildFontFamilies(Array.Empty<(string, string)>());
                return;
            }
            string families = Path.Combine(versionDir, ModCatalog.FamiliesFolder.Replace('/', '\\'));
            var files = Directory.Exists(families)
                ? Directory.EnumerateFiles(families, "*.json").Select(file =>
                {
                    string name = Path.GetFileName(file);
                    string backup = Path.Combine(originals, ModCatalog.FamiliesFolder.Replace('/', '\\'), name);
                    return (name, File.ReadAllText(File.Exists(backup) ? backup : file));
                }).ToList()
                : new List<(string, string)>();
            ModCatalog.RebuildFontFamilies(files);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("Bootstrapper::RefreshCustomFont", ex.Message);
        }
    }

    /// <summary>
    /// Roblox keeps its automatic graphics level per game in %LocalAppData%\Roblox\frm.cfg. After a
    /// pinned quality is removed, every game would start from that pinned level (quality 1 looks
    /// blurry), so the file goes and Roblox rebuilds it. Retried next launch if Roblox holds it.
    /// </summary>
    private static void ResetQualityCacheIfAsked()
    {
        if (!Store.State.ResetQualityCache)
            return;
        string cache = Path.Combine(Paths.LocalAppData, "Roblox", "frm.cfg");
        try
        {
            if (File.Exists(cache))
                File.Delete(cache);
            Store.State.ResetQualityCache = false;
            Logger.Info("Bootstrapper::ResetQualityCache", "Cleared Roblox's per-game graphics level cache");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn("Bootstrapper::ResetQualityCache", ex.Message);
        }
    }

    private static void WriteFastFlags(string versionDir)
    {
        string dir = Path.Combine(versionDir, "ClientSettings");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "ClientAppSettings.json"),
            JsonSerializer.Serialize(Store.Settings.FastFlags, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void StartRoblox(string versionDir)
    {
        // Multi-instance: a window already open from this folder would take this launch over
        // (Roblox's NoReload), so the new one starts from a folder of its own.
        bool multi = Store.Settings.MultiInstance && !Studio;
        string folder = multi ? MultiInstance.PlayerFolder(versionDir) : versionDir;
        var psi = new ProcessStartInfo(Path.Combine(folder, Executable))
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
        };
        // One argument, quoted if needed: a link with a space or quote can't split into several.
        if (launch.RobloxArgs.Length > 0)
            psi.ArgumentList.Add(launch.RobloxArgs);

        // Only the player is followed afterwards (Discord, server notice, history); Studio isn't.
        if (!Studio)
            LaunchedAt = DateTime.UtcNow;
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"{AppName} did not start");
        Logger.Info("Bootstrapper::StartRoblox", $"Started {AppName} (PID {process.Id}){(string.Equals(Path.GetFullPath(folder), Path.GetFullPath(versionDir), StringComparison.OrdinalIgnoreCase) ? "" : $" from {folder}")}");
        // Until the player has claimed its folder, a launch right after this one would pick the same.
        if (multi)
            MultiInstance.WaitUntilRegistered(folder, TimeSpan.FromSeconds(10));
    }

    private static bool IsRunningFrom(string versionDir, string executable)
    {
        string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionDir)) + Path.DirectorySeparatorChar;
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName?.StartsWith(dir, StringComparison.OrdinalIgnoreCase) == true)
                        return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // Exited meanwhile, or not ours to inspect.
                }
            }
        }
        return false;
    }

    /// <summary>Deletes every version folder but the player's, Studio's and a pinned one.</summary>
    private static void CleanupOldVersions(params string?[] keep)
    {
        foreach (string dir in Directory.EnumerateDirectories(Paths.Versions))
        {
            if (keep.Any(k => k is not null && Path.GetFileName(dir).Equals(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                Logger.Info("Bootstrapper::CleanupOldVersions", $"Deleted {dir}");
            }
            catch (Exception ex)
            {
                Logger.Warn("Bootstrapper::CleanupOldVersions", $"Failed to delete {dir}: {ex.Message}");
            }
        }
    }

    private static void CleanupDownloadCache(IEnumerable<string> keepSignatures)
    {
        var wanted = keepSignatures.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(Paths.Downloads))
            if (!wanted.Contains(Path.GetFileName(file)))
                TryDelete(file);
    }

    private static string Md5Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static string Mb(long bytes) => $"{bytes / 1048576.0:0.0} MB";

    private void Report(BootstrapPhase phase, string status, double? fraction = null, string? detail = null) =>
        progress.Report(new BootstrapProgress(phase, status, fraction, detail));
}
