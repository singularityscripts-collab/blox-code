using System.Net.Http;
using System.Security.Cryptography;

namespace BloxCode.Core;

/// <summary>
/// While you play, fetches the packages of a newer Roblox version (if one came out) into the
/// download cache, like Fishstrap's background updates. The next launch then finds them there and
/// only has to unpack, instead of downloading 100+ MB while you wait. Nothing is installed or
/// replaced while Roblox runs: the cache is the same one launches use, each file is checked against
/// its MD5 before it counts, and an unfinished file never takes the real name.
/// </summary>
public static class Prefetch
{
    /// <summary>How long a game has to be running before the check, so a quick launch costs nothing.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMinutes(2);

    /// <summary>Downloads what the newer version needs; returns how many packages were fetched (0 when already current).</summary>
    /// <param name="limit">Only the first (smallest) packages: for tests, which shouldn't download all of Roblox.</param>
    public static async Task<int> RunAsync(HttpClient http, CancellationToken ct, int limit = int.MaxValue)
    {
        const string ident = "Prefetch::Run";
        if (!Store.Settings.PrefetchUpdates || Store.Settings.PinnedVersion is not null || Store.State.VersionGuid is null)
            return 0;

        await Deployment.InitializeConnectivity(http, ct);
        var info = await Deployment.GetInfo(http, Store.Settings.Channel, ct);
        if (info.VersionGuid == Store.State.VersionGuid)
        {
            Logger.Info(ident, $"Roblox is current ({info.VersionGuid})");
            return 0;
        }

        string manifest = await http.GetStringAsync(Deployment.GetLocation($"/{info.VersionGuid}-rbxPkgManifest.txt"), ct);
        var packages = PackageManifest.Parse(manifest).Where(p => PackageManifest.PlayerDirectoryMap.ContainsKey(p.Name)).ToList();
        Directory.CreateDirectory(Paths.Downloads);
        Logger.Info(ident, $"Roblox {info.VersionGuid} is out; fetching its {packages.Count} packages in the background");

        int fetched = 0;
        foreach (var package in packages.OrderBy(p => p.PackedSize).Take(limit))
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(package.DownloadPath) && Md5(package.DownloadPath) == package.Signature)
                continue;

            string partial = package.DownloadPath + ".prefetch";
            try
            {
                using (var response = await http.GetAsync(Deployment.GetLocation($"/{info.VersionGuid}-{package.Name}"), HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var source = await response.Content.ReadAsStreamAsync(ct);
                    await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
                    await source.CopyToAsync(file, ct);
                }
                if (Md5(partial) != package.Signature)
                    throw new InvalidDataException($"{package.Name} failed its checksum");
                File.Move(partial, package.DownloadPath, overwrite: true);
                fetched++;
            }
            finally
            {
                try { File.Delete(partial); } catch { }
            }
        }
        Logger.Info(ident, $"Roblox {info.VersionGuid} is ready to install ({fetched} packages fetched)");
        return fetched;
    }

    private static string Md5(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(MD5.HashData(stream));
    }
}
