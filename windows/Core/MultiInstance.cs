using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BloxCode.Core;

/// <summary>
/// Lets several Roblox players run side by side. Roblox 0.746 keeps itself to one window in two ways,
/// and both have to be beaten:
/// <list type="number">
/// <item><b>ROBLOX_singletonEvent</b> (the classic one): each new player creates or opens this named
/// event and the older player closes ("waitForNewPlayerProcess"). Holding a <em>mutex</em> under that
/// name first makes the event creation fail ("Cannot create event to secure single process: 0x6"),
/// so no player finds another. This is MultiBloxy's mechanism (github.com/Zgoly/MultiBloxy, read from
/// its 1.1.0 build with REA).</item>
/// <item><b>NoReload</b> (new, and why MultiBloxy 1.1.0 no longer works): a starting player looks for
/// one already open <em>from the same executable path</em>, through a mutex and shared memory named
/// after that path ("C:_…_RobloxPlayerBeta.exe.mtx" / ".shm"). If it finds one it hands its launch
/// over ("NoReload Detector: App NoReload!") and quits. Those objects can't be taken in advance (the
/// player crashes without them), so every extra window starts from a path of its own instead: a
/// junction in InstancesN pointing at the same version folder. Same files, mods and Fast Flags,
/// nothing copied, and each window has its own NoReload channel.</item>
/// </list>
/// Found by reading the player's logs and listing the named objects a running player holds, then
/// confirmed one change at a time with two real players. Further changes from MultiBloxy:
/// <list type="bullet">
/// <item>No tray app: the launch that starts Roblox takes the name, and the background watcher keeps
/// it until the last player closes (a newer launch opens the same mutex before the old one lets go).</item>
/// <item>A player that was already open owns the name as an event. Its handle is closed from outside,
/// like MultiBloxy's "Fix", but with the 64-bit handle table (SystemExtendedHandleInformation), so
/// process ids above 65535 are not truncated, and a name is asked only of handles whose type is
/// Event, so NtQueryObject is never pointed at a pipe that could block it.</item>
/// </list>
/// </summary>
public static class MultiInstance
{
    public const string SingletonName = "ROBLOX_singletonEvent";
    private const string PlayerProcess = "RobloxPlayerBeta";

    private static Mutex? _held;

    public static bool IsHeld => _held is not null;

    public enum Outcome { Held, TookOverFromRoblox, Failed }

    /// <summary>Takes the singleton name for this process (kept until it exits). Safe to call more than once.</summary>
    public static Outcome Acquire()
    {
        const string ident = "MultiInstance::Acquire";
        if (_held is not null)
            return Outcome.Held;

        if (TryCreate())
        {
            Logger.Info(ident, "Holding ROBLOX_singletonEvent: Roblox players won't close each other");
            return Outcome.Held;
        }

        // An open player owns the name as an event: close its handle so the name frees up.
        int closed = CloseRobloxSingletonHandles();
        Logger.Info(ident, $"Closed {closed} ROBLOX_singletonEvent handle(s) in running players");
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (TryCreate())
            {
                Logger.Info(ident, "Took ROBLOX_singletonEvent over from the open player");
                return Outcome.TookOverFromRoblox;
            }
            Thread.Sleep(100);
        }

        Logger.Warn(ident, "Couldn't take ROBLOX_singletonEvent; this launch replaces the open player");
        return Outcome.Failed;
    }

    /// <summary>Extra windows have their own path; this many at most.</summary>
    public const int MaxWindows = 12;

    public static string InstancesFolder => Path.GetFullPath(Path.Combine(Paths.Base, "Instances"));

    /// <summary>
    /// The folder the next player should start from: the version folder itself when no player runs
    /// from it, otherwise the first InstancesN junction (made or re-pointed as needed) that is free.
    /// </summary>
    public static string PlayerFolder(string versionDir)
    {
        const string ident = "MultiInstance::PlayerFolder";
        // Full, backslash-only paths: what the player is named after, and what mklink accepts.
        versionDir = Path.GetFullPath(versionDir);
        if (!InUse(versionDir))
            return versionDir;
        for (int slot = 2; slot <= MaxWindows; slot++)
        {
            string folder = Path.Combine(InstancesFolder, slot.ToString());
            if (InUse(folder))
                continue;
            try
            {
                EnsureJunction(folder, versionDir);
                Logger.Info(ident, $"Window {slot} starts from {folder}");
                return folder;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Logger.Warn(ident, $"Couldn't prepare {folder}: {ex.Message}");
            }
        }
        Logger.Warn(ident, "No free window slot; this launch goes to an open window");
        return versionDir;
    }

    /// <summary>
    /// Waits until the player started from <paramref name="folder"/> has registered its path, so a
    /// launch right after this one picks the next slot instead of the same one.
    /// </summary>
    public static void WaitUntilRegistered(string folder, TimeSpan max)
    {
        var until = DateTime.UtcNow + max;
        while (DateTime.UtcNow < until && !InUse(folder))
            Thread.Sleep(250);
    }

    /// <summary>The player's own per-path mutex: it exists exactly while a player runs from <paramref name="folder"/>.</summary>
    public static string PathMutexName(string folder) =>
        // Windows hands the player its path normalised (backslashes only), which is what it names this after.
        Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)), PlayerProcess + ".exe").Replace('\\', '_') + ".mtx";

    public static bool InUse(string folder)
    {
        try
        {
            if (!Mutex.TryOpenExisting(PathMutexName(folder), out var existing))
                return false;
            existing.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            return true;   // Something holds the name: treat it as taken.
        }
    }

    /// <summary>InstancesN as a junction to the version folder (re-pointed after a Roblox update).</summary>
    private static void EnsureJunction(string folder, string versionDir)
    {
        var info = new DirectoryInfo(folder);
        if (info.Exists)
        {
            if (info.LinkTarget is { } target
                && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(versionDir)), StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(folder, PlayerProcess + ".exe")))
                return;
            // A junction is removed on its own, never followed; a plain folder here is ours to clear.
            if (info.LinkTarget is not null)
                Directory.Delete(folder);
            else
                Directory.Delete(folder, recursive: true);
        }
        Directory.CreateDirectory(InstancesFolder);

        // mklink /J needs no administrator rights or developer mode, unlike a symbolic link.
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add("mklink");
        psi.ArgumentList.Add("/J");
        psi.ArgumentList.Add(folder);
        psi.ArgumentList.Add(versionDir);
        using var mklink = Process.Start(psi) ?? throw new InvalidOperationException("cmd.exe didn't start");
        string error = mklink.StandardError.ReadToEnd();
        mklink.WaitForExit(10000);
        if (!File.Exists(Path.Combine(folder, PlayerProcess + ".exe")))
            throw new IOException($"mklink /J failed: {error.Trim()}");
    }

    /// <summary>Removes the Instances junctions no player is using (after "Close all", and on uninstall).</summary>
    public static void RemoveIdleJunctions()
    {
        if (!Directory.Exists(InstancesFolder))
            return;
        foreach (var dir in new DirectoryInfo(InstancesFolder).EnumerateDirectories())
        {
            if (InUse(dir.FullName))
                continue;
            try
            {
                if (dir.LinkTarget is not null)
                    Directory.Delete(dir.FullName);
            }
            catch (IOException) { }
        }
    }

    /// <summary>Closes every Roblox player (the "Close all" button).</summary>
    public static int CloseAllPlayers()
    {
        int count = 0;
        foreach (var process in Process.GetProcessesByName(PlayerProcess))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    count++;
                }
                catch (Exception ex)
                {
                    Logger.Warn("MultiInstance::CloseAll", $"PID {process.Id}: {ex.Message}");
                }
            }
        }
        return count;
    }

    /// <summary>How many Roblox players are open, the hidden tray ones included.</summary>
    public static int RunningPlayers()
    {
        var players = Process.GetProcessesByName(PlayerProcess);
        foreach (var p in players)
            p.Dispose();
        return players.Length;
    }

    private static bool TryCreate()
    {
        try
        {
            // Opening the mutex another Blox Code already made is fine too: the name stays a mutex.
            _held = new Mutex(false, SingletonName);
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;   // The name exists as Roblox's event.
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- Closing the event handle inside running players ------------------------------------

    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const uint ProcessDupHandle = 0x0040;
    private const uint DuplicateCloseSource = 0x1;
    private const uint DuplicateSameAccess = 0x2;

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleEntry
    {
        public IntPtr Object;
        public UIntPtr UniqueProcessId;
        public UIntPtr HandleValue;
        public uint GrantedAccess;
        public ushort CreatorBackTraceIndex;
        public ushort ObjectTypeIndex;
        public uint HandleAttributes;
        public uint Reserved;
    }

    /// <summary>Closes ROBLOX_singletonEvent in every running player; returns how many handles were closed.</summary>
    public static int CloseRobloxSingletonHandles()
    {
        var pids = new HashSet<ulong>();
        foreach (var process in Process.GetProcessesByName(PlayerProcess))
            using (process)
                pids.Add((ulong)process.Id);
        if (pids.Count == 0)
            return 0;

        IntPtr table = QueryHandleTable(out int length);
        if (table == IntPtr.Zero)
            return 0;

        var opened = new Dictionary<ulong, IntPtr>();
        int closed = 0;
        try
        {
            long count = (long)Marshal.ReadIntPtr(table);
            int headerSize = IntPtr.Size * 2;
            int entrySize = Marshal.SizeOf<HandleEntry>();
            count = Math.Min(count, (length - headerSize) / entrySize);
            ushort? eventType = null;

            for (long i = 0; i < count; i++)
            {
                var entry = Marshal.PtrToStructure<HandleEntry>(table + headerSize + (int)(i * entrySize));
                ulong pid = (ulong)entry.UniqueProcessId;
                if (!pids.Contains(pid))
                    continue;
                // Once the Event type index is known, skip every other kind of handle without asking.
                if (eventType is { } known && entry.ObjectTypeIndex != known)
                    continue;

                if (!opened.TryGetValue(pid, out IntPtr process))
                {
                    process = OpenProcess(ProcessDupHandle, false, (uint)pid);
                    opened[pid] = process;
                }
                if (process == IntPtr.Zero)
                    continue;

                var source = (IntPtr)(long)(ulong)entry.HandleValue;
                if (!DuplicateHandle(process, source, GetCurrentProcess(), out IntPtr copy, 0, false, DuplicateSameAccess))
                    continue;
                try
                {
                    string? type = QueryUnicodeString(copy, ObjectTypeInformation);
                    if (type != "Event")
                        continue;
                    eventType ??= entry.ObjectTypeIndex;

                    string? name = QueryUnicodeString(copy, ObjectNameInformation);
                    if (name is null || !name.EndsWith("\\" + SingletonName, StringComparison.Ordinal))
                        continue;
                }
                finally
                {
                    CloseHandle(copy);
                }

                // DUPLICATE_CLOSE_SOURCE with no target closes the handle inside the player.
                if (DuplicateHandle(process, source, IntPtr.Zero, out _, 0, false, DuplicateCloseSource))
                    closed++;
            }
        }
        finally
        {
            foreach (var process in opened.Values)
                if (process != IntPtr.Zero)
                    CloseHandle(process);
            Marshal.FreeHGlobal(table);
        }
        return closed;
    }

    private static IntPtr QueryHandleTable(out int length)
    {
        length = 1 << 20;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            IntPtr buffer = Marshal.AllocHGlobal(length);
            uint status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, length, out int needed);
            if (status == 0)
                return buffer;
            Marshal.FreeHGlobal(buffer);
            if (status != StatusInfoLengthMismatch)
            {
                Logger.Warn("MultiInstance::HandleTable", $"NtQuerySystemInformation failed: 0x{status:X8}");
                return IntPtr.Zero;
            }
            // The table grows between calls; leave room.
            length = Math.Max(length * 2, needed + (1 << 16));
        }
        return IntPtr.Zero;
    }

    /// <summary>Reads the UNICODE_STRING that heads OBJECT_NAME_INFORMATION and OBJECT_TYPE_INFORMATION.</summary>
    private static string? QueryUnicodeString(IntPtr handle, int infoClass)
    {
        int length = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            uint status = NtQueryObject(handle, infoClass, buffer, length, out int needed);
            if (status == StatusInfoLengthMismatch && needed > length && needed < 1 << 16)
            {
                Marshal.FreeHGlobal(buffer);
                length = needed;
                buffer = Marshal.AllocHGlobal(length);
                status = NtQueryObject(handle, infoClass, buffer, length, out _);
            }
            if (status != 0)
                return null;
            ushort bytes = (ushort)Marshal.ReadInt16(buffer);
            IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);   // after Length, MaximumLength and padding
            return bytes == 0 || text == IntPtr.Zero ? null : Marshal.PtrToStringUni(text, bytes / 2);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
