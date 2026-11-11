using System.Diagnostics;

namespace BloxCode.Core;

/// <summary>
/// Programs that open together with Roblox (OBS, a FPS counter, a macro tool) and, if wanted, close
/// when the last Roblox window does, like Bloxstrap's custom integrations. A program that is already
/// open isn't started twice, and only the copies Blox Code started are ever closed: each is
/// remembered by process ID and start time, so a reused ID never closes someone else's program.
/// </summary>
public static class Integrations
{
    /// <summary>Starts every configured program that isn't running yet. Never throws: one bad entry must not stop a launch.</summary>
    public static void StartAll()
    {
        foreach (var app in Store.Settings.Integrations.ToList())
        {
            try
            {
                if (!File.Exists(app.Path))
                {
                    Logger.Warn("Integrations::Start", $"{app.Name}: {app.Path} no longer exists");
                    continue;
                }
                if (IsRunning(app.Path))
                {
                    Logger.Info("Integrations::Start", $"{app.Name} is already open");
                    continue;
                }
                var process = Process.Start(new ProcessStartInfo(app.Path, app.Arguments ?? "")
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(app.Path)!,
                });
                if (process is null)
                    continue;
                Logger.Info("Integrations::Start", $"Started {app.Name} (PID {process.Id})");
                if (app.CloseWithRoblox)
                {
                    Store.State.IntegrationProcesses.Add(new StartedIntegration { Pid = process.Id, StartedAt = process.StartTime.ToUniversalTime(), Path = app.Path });
                    Store.SaveState();
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn("Integrations::Start", $"{app.Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Closes the programs Blox Code started (politely first), once Roblox is closed.</summary>
    public static void CloseAll()
    {
        var started = Store.State.IntegrationProcesses.ToList();
        if (started.Count == 0)
            return;
        Store.State.IntegrationProcesses.Clear();
        Store.SaveState();

        foreach (var entry in started)
        {
            try
            {
                using var process = Process.GetProcessById(entry.Pid);
                if (process.HasExited || Math.Abs((process.StartTime.ToUniversalTime() - entry.StartedAt).TotalSeconds) > 2)
                    continue;   // gone, or the ID now belongs to another program
                Logger.Info("Integrations::Close", $"Closing {Path.GetFileName(entry.Path)} (PID {entry.Pid})");
                if (!process.CloseMainWindow() || !process.WaitForExit(5000))
                    process.Kill();
            }
            catch (ArgumentException)
            {
                // Already closed.
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Logger.Warn("Integrations::Close", $"PID {entry.Pid}: {ex.Message}");
            }
        }
    }

    public static bool WantsWatcher => Store.Settings.Integrations.Any(i => i.CloseWithRoblox);

    private static bool IsRunning(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    return true;   // can't look inside (elevated): same name is close enough to not start a second one
                }
            }
        }
        return false;
    }
}
