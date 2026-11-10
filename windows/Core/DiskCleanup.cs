namespace BloxCode.Core;

/// <summary>
/// "Free up space": Roblox versions other than the current (and pinned) one, the package download
/// cache, previews in the temp cache, and logs older than a week (Roblox's and Blox Code's own).
/// Anything in use is skipped, never forced.
/// </summary>
public static class DiskCleanup
{
    private static readonly TimeSpan LogAge = TimeSpan.FromDays(7);

    public static long Measure() => Targets().Sum(t => t.Bytes);

    /// <summary>Deletes everything <see cref="Measure"/> counts; returns the bytes actually freed.</summary>
    public static long Run()
    {
        long freed = 0;
        foreach (var target in Targets())
        {
            try
            {
                if (Directory.Exists(target.Path))
                    Directory.Delete(target.Path, recursive: true);
                else
                    File.Delete(target.Path);
                freed += target.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A running Roblox version or a log being written: leave it for next time.
                Logger.Info("DiskCleanup::Run", $"Skipped {target.Path}: {ex.Message}");
            }
        }
        Logger.Info("DiskCleanup::Run", $"Freed {Size(freed)}");
        return freed;
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        _ => $"{bytes / 1024.0:0} KB",
    };

    private sealed record Target(string Path, long Bytes);

    private static IEnumerable<Target> Targets()
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Store.State.VersionGuid is { } current) keep.Add(current);
        if (Store.Settings.PinnedVersion is { } pinned) keep.Add(pinned);
        if (Store.State.StudioVersionGuid is { } studio) keep.Add(studio);

        if (Directory.Exists(Paths.Versions))
            foreach (var dir in new DirectoryInfo(Paths.Versions).EnumerateDirectories().Where(d => !keep.Contains(d.Name)))
                yield return new Target(dir.FullName, SizeOf(dir));

        if (Directory.Exists(Paths.Downloads))
            foreach (var file in new DirectoryInfo(Paths.Downloads).EnumerateFiles())
                yield return new Target(file.FullName, file.Length);

        if (Directory.Exists(Paths.Cache))
            foreach (var dir in new DirectoryInfo(Paths.Cache).EnumerateDirectories())
                yield return new Target(dir.FullName, SizeOf(dir));

        foreach (string logs in new[] { RobloxLog.Directory, Paths.Logs }.Where(Directory.Exists))
            foreach (var file in new DirectoryInfo(logs).EnumerateFiles("*.log").Where(f => DateTime.UtcNow - f.LastWriteTimeUtc > LogAge))
                yield return new Target(file.FullName, file.Length);
    }

    private static long SizeOf(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
