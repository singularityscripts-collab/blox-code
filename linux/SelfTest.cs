using System.IO.Compression;
using System.Text.Json.Nodes;
using BloxCode.Core;
using BloxCode.Linux.Platform;
using BloxCode.Linux.Runtime;

namespace BloxCode.Linux;

/// <summary>
/// `BloxCode --self-test`: headless checks of everything that touches files, run in a throwaway
/// home folder so the real Sober and Blox Code settings are never touched. CI runs it on every
/// supported distro; on a test PC it proves the AppImage itself works there.
/// </summary>
public static class SelfTest
{
    private static int _failures;

    public static int Run()
    {
        string sandbox = Path.Combine(Path.GetTempPath(), $"bloxcode-selftest-{Environment.ProcessId}");
        Directory.CreateDirectory(sandbox);

        // Must happen before anything reads Paths: every location below is derived from these.
        Environment.SetEnvironmentVariable("HOME", sandbox);
        Environment.SetEnvironmentVariable("BLOXCODE_LAYOUT", "xdg");
        foreach (string variable in new[] { "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME", "APPIMAGE", "APPDIR", "APPIMAGE_EXTRACT_AND_RUN" })
            Environment.SetEnvironmentVariable(variable, null);
        string configHome = Path.Combine(sandbox, "custom-config");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", OperatingSystem.IsWindows() ? null : configHome);

        Console.WriteLine($"Blox Code {Updater.Current} self-test ({Distro.Current.Name}, {Environment.OSVersion})");
        try
        {
            Check("paths follow XDG inside HOME", () =>
            {
                Expect(Paths.IsXdg, "XDG layout");
                Expect(Paths.Base.StartsWith(sandbox), $"data dir {Paths.Base}");
                Expect(Paths.Application == Path.Combine(sandbox, ".local", "bin", "bloxcode"), $"app {Paths.Application}");
                if (!OperatingSystem.IsWindows())
                    Expect(Paths.Config == Path.Combine(configHome, "bloxcode"), $"XDG_CONFIG_HOME honoured ({Paths.Config})");
                Expect(Sober.ConfigFile.StartsWith(sandbox), "Sober config inside HOME");
                Paths.EnsureCreated();
                Logger.Initialize();
                Expect(Logger.FilePath is not null && File.Exists(Logger.FilePath), "log file");
            });

            Check("settings round trip", () =>
            {
                Store.Load();
                Store.Settings.FastFlags["FFlagSelfTest"] = "True";
                Store.SaveSettings();
                Store.Load();
                Expect(Store.Settings.FastFlags.GetValueOrDefault("FFlagSelfTest") == "True", "flag survived");
                Store.Settings.FastFlags.Clear();
                Store.SaveSettings();
            });

            Check("graphics presets use only allow-listed flags and read back", () =>
            {
                Store.Settings.FastFlags.Clear();
                Expect(FastFlagPresets.PresetFlags.All(FastFlagPresets.IsAllowlisted), "every preset flag is allow-listed");
                Expect(FastFlagPresets.CurrentGraphicsPreset == GraphicsPreset.Auto, "no flags is Auto");
                foreach (var preset in Enum.GetValues<GraphicsPreset>())
                {
                    FastFlagPresets.ApplyGraphicsPreset(preset);
                    Expect(FastFlagPresets.CurrentGraphicsPreset == preset, $"{preset} reads back");
                    Expect(Store.Settings.FastFlags.Keys.All(FastFlagPresets.IsAllowlisted), $"{preset} writes allow-listed flags");
                }
                FastFlagPresets.MsaaSamples = "2";
                Expect(FastFlagPresets.CurrentGraphicsPreset is null, "a hand-changed flag is Custom");
                FastFlagPresets.ApplyGraphicsPreset(GraphicsPreset.Auto);
                FastFlagPresets.MsaaSamples = null;
                Expect(Store.Settings.FastFlags.Count == 0, "Auto clears everything");

                FastFlagPresets.Renderer = RenderingMode.Vulkan;
                Expect(FastFlagPresets.Renderer == RenderingMode.Vulkan && Store.Settings.FastFlags.Count == 1, "one renderer at a time");
                FastFlagPresets.Renderer = RenderingMode.OpenGL;
                Expect(FastFlagPresets.Get("FFlagDebugGraphicsPreferOpenGL") == "True" && FastFlagPresets.Get("FFlagDebugGraphicsPreferVulkan") is null, "switching clears the old one");
                FastFlagPresets.Renderer = RenderingMode.Default;
                Expect(Store.Settings.FastFlags.Count == 0, "Default clears it");

                // 1.2.0's blurry Lowest (quality 1, textures 0) is moved to the sharp one.
                FastFlagPresets.ApplyGraphicsPreset(GraphicsPreset.Lowest);
                FastFlagPresets.Set(FastFlagPresets.QualityLevel, "1");
                FastFlagPresets.Set(FastFlagPresets.TextureQuality, "0");
                Expect(FastFlagPresets.UpgradeBlurryPresets() && FastFlagPresets.CurrentGraphicsPreset == GraphicsPreset.Lowest, "old Lowest upgraded");
                Expect(!FastFlagPresets.UpgradeBlurryPresets(), "upgrade runs once");
                // The Medium that forced textures to 2 now leaves them to Roblox.
                FastFlagPresets.ApplyGraphicsPreset(GraphicsPreset.Auto);
                FastFlagPresets.Set(FastFlagPresets.QualityLevel, "11");
                FastFlagPresets.TextureLevel = "2";
                Expect(FastFlagPresets.UpgradeBlurryPresets() && FastFlagPresets.CurrentGraphicsPreset == GraphicsPreset.Medium
                       && FastFlagPresets.TextureLevel is null, "old Medium upgraded");
                FastFlagPresets.ApplyGraphicsPreset(GraphicsPreset.Auto);

                // Suggestions from the graphics card.
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("Intel(R) HD Graphics 5500", GpuVendor.Intel, null)) == GraphicsPreset.Lowest, "old Intel HD -> Lowest");
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, 128L << 20)) == GraphicsPreset.Low, "Iris Xe -> Low");
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("Intel(R) Arc(TM) A750 Graphics", GpuVendor.Intel, 8L << 30)) == GraphicsPreset.High, "Arc A750 -> High");
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("AMD Radeon(TM) Graphics", GpuVendor.Amd, 512L << 20)) == GraphicsPreset.Low, "AMD APU -> Low");
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("NVIDIA GeForce GTX 1050", GpuVendor.Nvidia, 2L << 30)) == GraphicsPreset.Medium, "2 GB card -> Medium");
                Expect(GraphicsAdvisor.Recommend(new GpuInfo("AMD Radeon RX 6600", GpuVendor.Amd, 8L << 30)) == GraphicsPreset.High, "8 GB card -> High");
            });

            Check("pick-a-file mods and the custom font", () =>
            {
                string mods = Path.Combine(Paths.Cache, "catalog-test");
                string picked = Path.Combine(Paths.Cache, "picked.ogg");
                Directory.CreateDirectory(Paths.Cache);
                File.WriteAllText(picked, "oof");
                var death = ModCatalog.Slots.Single(s => s.Id == "death");
                var cursor = ModCatalog.Slots.Single(s => s.Id == "cursor");
                var font = ModCatalog.Slots.Single(s => s.Kind == ModKind.Font);

                ModCatalog.Apply(death, picked, mods);
                Expect(ModCatalog.CurrentFile(death, mods) is { } f && File.ReadAllText(f) == "oof", "sound copied into place");
                Expect(ModSync.ListMods(mods).SequenceEqual(new[] { "content/sounds/ouch.ogg" }), "lands at Roblox's path");
                bool refused = false;
                try { ModCatalog.Apply(cursor, picked, mods); } catch (InvalidOperationException) { refused = true; }
                Expect(refused, "wrong file type refused");

                string png = Path.Combine(Paths.Cache, "cursor.png");
                File.WriteAllBytes(png, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                ModCatalog.Apply(cursor, png, mods);
                Expect(cursor.Targets.All(t => File.Exists(Path.Combine(mods, t))), "cursor fills both arrow files");

                string ttf = Path.Combine(Paths.Cache, "font.ttf");
                File.WriteAllText(ttf, "font");
                ModCatalog.Apply(font, ttf, mods);
                var families = new[] { ("Arimo.json", "{\"name\":\"Arimo\",\"faces\":[{\"name\":\"Regular\",\"assetId\":\"rbxasset://fonts/Arimo-Regular.ttf\"},{\"name\":\"Bold\",\"assetId\":\"rbxassetid://1\"}]}"), ("../evil.json", "{}") };
                Expect(ModCatalog.RebuildFontFamilies(families, mods) == 1, "families rewritten, unsafe names skipped");
                string arimo = File.ReadAllText(Path.Combine(mods, "content", "fonts", "families", "Arimo.json"));
                Expect(arimo.Split("rbxasset://fonts/CustomFont.ttf").Length == 3, "every face points at the custom font");

                ModCatalog.Reset(font, mods);
                Expect(!Directory.Exists(Path.Combine(mods, "content", "fonts")), "font reset removes the font and its families");
                ModCatalog.Reset(death, mods);
                ModCatalog.Reset(cursor, mods);
                Expect(!Directory.EnumerateFileSystemEntries(mods).Any(), "resets leave nothing behind");
            });

            Check("flag values become JSON types", () =>
            {
                Expect(SoberConfig.ToJson("FFlagX", "True").GetValueKind() == System.Text.Json.JsonValueKind.True, "bool");
                Expect(SoberConfig.ToJson("DFIntX", "144").ToJsonString() == "144", "int");
                Expect(SoberConfig.ToJson("FStringX", "true").ToJsonString() == "\"true\"", "string stays string");
                Expect(SoberConfig.ToJson("FFlagX_PlaceFilter", "True;1818").ToJsonString() == "\"True;1818\"", "filter stays string");
                Expect(SoberConfig.ToJson("FIntX", "abc").ToJsonString() == "\"abc\"", "bad int stays string");
                Expect(SoberConfig.FromJson("DFIntX", JsonValue.Create(60)) == "60", "number back to text");
                Expect(SoberConfig.FromJson("FFlagX", JsonValue.Create(false)) == "False", "bool back to text");
            });

            Check("Sober config: merge, import once, keep unknown keys, back up", () =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Sober.ConfigFile)!);
                File.WriteAllText(Sober.ConfigFile, """
                    {
                      // Sober writes plain JSON; comments and trailing commas must not break us
                      "use_opengl": false,
                      "some_future_key": { "nested": [1, 2] },
                      "fflags": { "DFIntTaskSchedulerTargetFps": 144, "FFlagMine": true, },
                    }
                    """);
                Store.State.SoberFlagsImported = false;
                Store.Settings.FastFlags = new() { ["FIntDebugForceMSAASamples"] = "4" };

                Expect(SoberConfig.ApplyFastFlags(), "first apply writes");
                var root = SoberConfig.Load();
                var fflags = root["fflags"]!.AsObject();
                Expect(fflags["DFIntTaskSchedulerTargetFps"]!.ToJsonString() == "144", "Sober's flag imported and kept as a number");
                Expect(fflags["FFlagMine"]!.GetValueKind() == System.Text.Json.JsonValueKind.True, "Sober's bool kept");
                Expect(fflags["FIntDebugForceMSAASamples"]!.ToJsonString() == "4", "Blox Code's flag written as a number");
                Expect(root["some_future_key"]?["nested"]?.AsArray().Count == 2, "unknown key preserved");
                Expect(Store.Settings.FastFlags.ContainsKey("FFlagMine"), "imported into settings");
                Expect(File.Exists(SoberConfig.BackupFile), "original backed up");
                Expect(!SoberConfig.ApplyFastFlags(), "second apply is a no-op");

                Store.Settings.FastFlags.Remove("FFlagMine");
                SoberConfig.ApplyFastFlags();
                Expect(SoberConfig.Load()["fflags"]!.AsObject().ContainsKey("FFlagMine") == false, "deleted flag leaves Sober (no re-import)");
            });

            Check("Sober config: invalid JSON is reported and left alone", () =>
            {
                File.WriteAllText(Sober.ConfigFile, "{ \"fflags\": { oops ");
                bool threw = false;
                try { SoberConfig.ApplyFastFlags(); }
                catch (SoberConfigException) { threw = true; }
                Expect(threw, "SoberConfigException");
                Expect(File.ReadAllText(Sober.ConfigFile) == "{ \"fflags\": { oops ", "file untouched");
                SoberConfig.Reset();
                Expect(!File.Exists(Sober.ConfigFile), "reset deletes it");
                Expect(SoberConfig.ApplyFastFlags(), "writes defaults again");
                Expect(SoberConfig.GetString(SoberConfig.Load(), "graphics_optimization_mode", "") == "balanced", "defaults present");
            });

            Check("mods sync into the overlay and restore the user's own files", () =>
            {
                string rel = "content/textures/Cursors/KeyboardMouse/ArrowCursor.png";
                string mod = Path.Combine(Paths.Modifications, "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png");
                string overlay = Path.Combine(Sober.OverlayDir, "content", "textures", "Cursors", "KeyboardMouse", "ArrowCursor.png");
                string userOwn = Path.Combine(Sober.OverlayDir, "content", "sounds", "ouch.ogg");
                Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
                Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
                Directory.CreateDirectory(Path.GetDirectoryName(userOwn)!);
                File.WriteAllText(mod, "mine");
                File.WriteAllText(overlay, "user's hand-placed cursor");
                File.WriteAllText(userOwn, "unrelated user file");
                File.WriteAllText(Path.Combine(Paths.Modifications, "README.txt"), "notes");
                Store.State.ModManifest.Clear();

                var first = ModSync.Sync();
                Expect(first.Applied == 1 && first.Failed.Count == 0, $"applied {first.Applied}");
                Expect(File.ReadAllText(overlay) == "mine", "mod in overlay");
                Expect(Store.State.ModManifest.SequenceEqual(new[] { rel }), "manifest tracks it");
                Expect(!File.Exists(Path.Combine(Sober.OverlayDir, "README.txt")), "README not copied");
                Expect(ModSync.Sync().Applied == 0, "unchanged mod isn't copied again");

                File.Delete(mod);
                var second = ModSync.Sync();
                Expect(second.Removed == 1, "removed");
                Expect(File.ReadAllText(overlay) == "user's hand-placed cursor", "user's file restored");
                Expect(File.ReadAllText(userOwn) == "unrelated user file", "other overlay files untouched");
                Expect(Store.State.ModManifest.Count == 0, "manifest empty");

                Expect(!ModSync.IsSafe("../evil") && !ModSync.IsSafe("/etc/passwd") && !ModSync.IsSafe("a//b") && ModSync.IsSafe("a/b.png"), "path safety");
            });

            Check("APK assets are listed safely", () =>
            {
                string apk = Path.Combine(Sober.PackagesDir, "x86_64", "com.roblox.client", "base.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(apk)!);
                using (var zip = ZipFile.Open(apk, ZipArchiveMode.Create))
                {
                    foreach (string name in new[] { "assets/content/textures/Cursors/KeyboardMouse/ArrowCursor.png", "assets/content/sounds/ouch.ogg", "assets/../evil.txt", "lib/x86_64/libroblox.so", "assets/content/" })
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                        writer.Write("data");
                    }
                }
                Expect(ApkAssets.FindApk() == apk, "found under packages/*/com.roblox.client");
                var assets = ApkAssets.List(apk);
                Expect(assets.Count == 2, $"{assets.Count} assets (only safe files under assets/)");
                Expect(assets.Any(a => a.Path == "content/sounds/ouch.ogg" && a.Name == "ouch.ogg" && a.Folder == "content/sounds"), "names and folders");
                string extracted = Path.Combine(sandbox, "out.ogg");
                ApkAssets.Extract(apk, "content/sounds/ouch.ogg", extracted);
                Expect(File.ReadAllText(extracted) == "data", "extract");
            });

            Check("desktop entry and mimeapps.list", () =>
            {
                Expect(DesktopIntegration.QuoteExec("/home/u/.local/bin/bloxcode") == "/home/u/.local/bin/bloxcode", "plain path unquoted");
                Expect(DesktopIntegration.QuoteExec("/home/my user/bin/blox$code") == "\"/home/my user/bin/blox\\\\$code\"", "quoted and escaped");
                string entry = DesktopIntegration.DesktopEntry("/home/u/.local/bin/bloxcode");
                Expect(entry.Contains("Exec=/home/u/.local/bin/bloxcode %u\n") && entry.Contains("MimeType=x-scheme-handler/roblox;x-scheme-handler/roblox-player;"), "Exec and MimeType");
                Expect(DesktopIntegration.DesktopEntry("/home/u/.local/bin/bloxcode", extractAndRun: true)
                    .Contains("Exec=env APPIMAGE_EXTRACT_AND_RUN=1 /home/u/.local/bin/bloxcode %u\n"), "no-FUSE entry unpacks the AppImage");

                string list = Path.Combine(sandbox, "mimeapps.list");
                File.WriteAllText(list, "[Added Associations]\ntext/html=firefox.desktop;\n\n[Default Applications]\nx-scheme-handler/roblox=org.vinegarhq.Sober.desktop;\ntext/plain=gedit.desktop;\n");
                MimeApps.SetDefaults(list, DesktopIntegration.Schemes, DesktopIntegration.DesktopId);
                var defaults = MimeApps.Read(list);
                Expect(defaults["x-scheme-handler/roblox"] == "bloxcode.desktop" && defaults["x-scheme-handler/roblox-player"] == "bloxcode.desktop", "set");
                Expect(defaults["text/plain"] == "gedit.desktop" && File.ReadAllText(list).Contains("text/html=firefox.desktop;"), "other entries kept");
                MimeApps.RemoveDefaults(list, DesktopIntegration.Schemes, DesktopIntegration.DesktopId);
                Expect(!MimeApps.Read(list).ContainsKey("x-scheme-handler/roblox"), "removed");
            });

            Check("distro families", () =>
            {
                Expect(Distro.Load("NAME=\"CachyOS Linux\"\nID=cachyos\nID_LIKE=arch\n").Family == DistroFamily.Arch, "CachyOS is Arch");
                Expect(Distro.Load("ID=arch\n").Family == DistroFamily.Arch, "Arch");
                Expect(Distro.Load("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\n").Family == DistroFamily.Debian, "Mint is Debian");
                Expect(Distro.Load("ID=debian\nVERSION_ID=\"12\"\n").Family == DistroFamily.Debian, "Debian");
                Expect(Distro.Load("ID=fedora\n").Family == DistroFamily.Other, "Fedora is other");
                Expect(Distro.Load("PRETTY_NAME='Pop!_OS 22.04'\nID=pop\nID_LIKE=\"ubuntu debian\"").Name == "Pop!_OS 22.04", "quotes");
            });

            Check("flatpak output parsing", () =>
            {
                var info = Sober.ParseInfo("\nSober - Roblox on Linux\n\n          ID: org.vinegarhq.Sober\n     Version: 1.4.2\nInstallation: user\n");
                Expect(info["Version"] == "1.4.2" && info["Installation"] == "user", "flatpak info");
                var p = Sober.ParseProgress("Installing 2/3… ████████▌            45%  2.1 MB/s  00:12");
                Expect(p?.Fraction is > 0.48 and < 0.49, $"progress {p?.Fraction}");
                Expect(Sober.ParseProgress("Looking for matches…") is null, "no progress");
                Expect(Sober.NvidiaDriver("NVRM version: NVIDIA UNIX x86_64 Kernel Module  580.95.05  Thu Sep 25 2025") == "580.95.05", "driver version");
                Expect(Sober.NvidiaDriver("NVRM version: NVIDIA UNIX Open Kernel Module for x86_64  580.95.05  Release Build") == "580.95.05", "open module");
                Expect(Sober.ParseGlExtensions("org.gnome.Platform\norg.freedesktop.Platform.GL.nvidia-580-95-05\n").SequenceEqual(new[] { "580-95-05" }), "GL extension");
            });

            Check("launch arguments and links", () =>
            {
                var link = LaunchSettings.Parse(new[] { "roblox://experiences/start?placeId=1818" });
                Expect(link.Mode == LaunchMode.Player && link.RobloxArgs.EndsWith("1818"), "roblox:// opens the player");
                var upgrade = LaunchSettings.Parse(new[] { "-upgrade", "-player", "roblox-player:1+launchmode:play" });
                Expect(upgrade.Upgrade && upgrade.ForwardArgs.SequenceEqual(new[] { "-player", "roblox-player:1+launchmode:play" }), "upgrade forwards the rest");
                Expect(RobloxLink.ToLaunchArgs("https://www.roblox.com/games/920587237/Adopt-Me") == "roblox://experiences/start?placeId=920587237", "game URL");
                Expect(RobloxLink.ToLaunchArgs("not a game") is null && RobloxLink.ToLaunchArgs("") == "", "invalid and empty");
                Expect(Updater.AssetName == (OperatingSystem.IsWindows() ? "BloxCode.exe" : "BloxCode-x86_64.AppImage"), "update asset");
            });

            Check("signed update manifests", () =>
            {
                using var key = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
                string publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
                string Sign(byte[] data) => Convert.ToBase64String(key.SignData(data, System.Security.Cryptography.HashAlgorithmName.SHA256));
                string hash = new string('a', 64);
                byte[] manifest = System.Text.Encoding.UTF8.GetBytes($$$"""
                    {"format":1,"product":"BloxCode","version":"9.8.7","title":"Test","assets":{
                      "linux-x64":{"name":"BloxCode-x86_64.AppImage","size":5,"sha256":"{{{hash}}}","urls":["9.8.7/BloxCode-x86_64.AppImage","http://evil.example/x","https://mirror.example/b"]}} }
                    """);
                var feed = new Uri("https://updates.example/feed/latest.json");
                byte[] signature = System.Text.Encoding.ASCII.GetBytes(Sign(manifest) + "\n");

                var info = Updater.ReadManifest(manifest, signature, publicKey, feed, "linux-x64");
                Expect(info is { Version.Major: 9, Size: 5 } && info.Sha256 == hash, "a signed manifest reads");
                Expect(info!.DownloadUrls.SequenceEqual(new[] { "https://updates.example/feed/9.8.7/BloxCode-x86_64.AppImage", "https://mirror.example/b" }),
                    "relative addresses resolve next to the manifest; plain http is dropped");
                Expect(Updater.ReadManifest(manifest, signature, publicKey, feed, "windows-x64") is null, "a missing platform is no update");

                byte[] tampered = (byte[])manifest.Clone();
                tampered[Array.IndexOf(tampered, (byte)'9')] = (byte)'8';
                Expect(Throws(() => Updater.ReadManifest(tampered, signature, publicKey, feed, "linux-x64")), "a changed manifest is rejected");
                using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
                Expect(Throws(() => Updater.ReadManifest(manifest, signature, Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), feed, "linux-x64")), "another key is rejected");
                Expect(Throws(() => Updater.ReadManifest(manifest, "garbage"u8.ToArray(), publicKey, feed, "linux-x64")), "a broken signature is rejected");
                Expect(Updater.FeedFolder("https://a.example/f") == "https://a.example/f/" && Updater.FeedFolder("https://a.example/f/latest.json") == "https://a.example/f/", "feed addresses");
                Expect(Updater.PublicKey is not null, "this build trusts a release key");

                var notes = ReleaseNotes.Parse("# Log\n\n## 2.0.0 — Big one\n\n### Fixes\n\n- **Bold** fix with `code`\n  wrapped [link](https://x)\n\n## 1.0.0\n- old\n", new Version(2, 0, 0));
                Expect(notes is { Title: "Big one" } && notes.Value.Body == "FIXES\n\n• Bold fix with code wrapped link", $"changelog section to text ({notes?.Body})");
                Expect(ReleaseNotes.For(Updater.Current) is not null, $"the built-in changelog has notes for {Updater.Current}");
            });

            // Optional end-to-end check against a real feed: BLOXCODE_UPDATE_FEED=<folder address>.
            if (Environment.GetEnvironmentVariable("BLOXCODE_UPDATE_FEED") is { Length: > 0 } liveFeed)
            {
                Check($"update feed {liveFeed}", () =>
                {
                    using var http = new HttpClient();
                    var update = Updater.CheckAsync(http, CancellationToken.None).GetAwaiter().GetResult();
                    Expect(update is not null, $"the feed offers something newer than {Updater.Current}");
                    string file = Updater.DownloadAsync(http, update!, new Progress<(long, long)>(), CancellationToken.None).GetAwaiter().GetResult();
                    Expect(new FileInfo(file).Length == update!.Size, $"downloaded and verified {update.Version} ({file})");
                });
            }

            Check("one launch at a time", () =>
            {
                using (var first = InstanceLock.TryAcquire("selftest"))
                {
                    Expect(first is not null, "first acquires");
                    if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows())
                        Expect(InstanceLock.TryAcquire("selftest") is null, "second waits");
                }
                using var again = InstanceLock.TryAcquire("selftest");
                Expect(again is not null, "free after release");
            });

            Check("updates replace a running AppImage without touching it", () =>
            {
                string installed = Path.Combine(sandbox, "bin", "bloxcode");
                string update = Path.Combine(sandbox, "update.AppImage");
                Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
                File.WriteAllText(installed, "old");
                File.WriteAllText(update, "new");
                if (OperatingSystem.IsWindows())
                {
                    Paths.InstallExecutable(update, installed);
                }
                else
                {
                    // The running copy is mounted from its file; it must keep reading the old bytes.
                    using var running = new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    Paths.InstallExecutable(update, installed);
                    Expect(new StreamReader(running).ReadToEnd() == "old", "running copy unchanged");
                    Expect(File.GetUnixFileMode(installed).HasFlag(UnixFileMode.UserExecute), "executable");
                }
                Expect(File.ReadAllText(installed) == "new", "new copy in place");
                Expect(!File.Exists(installed + ".new"), "no temp file left");
            });

            Check("an unpacked AppImage (no FUSE) starts its next copy unpacked too", () =>
            {
                try
                {
                    Environment.SetEnvironmentVariable("APPIMAGE", Path.Combine(sandbox, "BloxCode-x86_64.AppImage"));
                    // CI runs this very test unpacked (no FUSE in containers), which the program path gives away.
                    bool selfUnpacked = (Environment.ProcessPath ?? "").Contains("/appimage_extracted", StringComparison.Ordinal);
                    Expect(Paths.IsExtractedAppImage == selfUnpacked
                           && Updater.StartInfo("bloxcode").Environment.ContainsKey("APPIMAGE_EXTRACT_AND_RUN") == selfUnpacked,
                        selfUnpacked ? "unpacked run detected from the program path" : "mounted: nothing added");
                    Environment.SetEnvironmentVariable("APPDIR", "/tmp/appimage_extracted_0123abcd");
                    Expect(Paths.IsExtractedAppImage, "detected from APPDIR");
                    Expect(Updater.StartInfo("bloxcode").Environment.TryGetValue("APPIMAGE_EXTRACT_AND_RUN", out var flag) && flag == "1", "passed to the next copy");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("APPIMAGE", null);
                    Environment.SetEnvironmentVariable("APPDIR", null);
                }
            });

            Check("install and uninstall on the desktop", () =>
            {
                string appImage = Path.Combine(sandbox, "Downloads", "BloxCode-x86_64.AppImage");
                Directory.CreateDirectory(Path.GetDirectoryName(appImage)!);
                File.WriteAllText(appImage, "#!/bin/sh\n");
                Environment.SetEnvironmentVariable("APPIMAGE", appImage);
                try
                {
                    Expect(DesktopIntegration.CanInstall && !DesktopIntegration.IsInstalled, "can install");
                    DesktopIntegration.InstallAsync(size => new byte[] { (byte)(size / 4) }).GetAwaiter().GetResult();
                    Expect(File.ReadAllText(Paths.Application) == "#!/bin/sh\n", "AppImage copied to ~/.local/bin/bloxcode");
                    Expect(DesktopIntegration.IsInstalled, "installed");
                    string unpack = Paths.IsExtractedAppImage ? "env APPIMAGE_EXTRACT_AND_RUN=1 " : "";
                    Expect(File.ReadAllText(DesktopIntegration.DesktopFile).Contains($"Exec={unpack}{DesktopIntegration.QuoteExec(Paths.Application)} %u"), "menu entry runs the installed copy");
                    Expect(File.Exists(DesktopIntegration.IconFile(256)) && File.Exists(DesktopIntegration.IconFile(48)), "icons");
                    Expect(File.Exists(Path.Combine(Paths.Modifications, "README.txt")), "mods readme");
                    Expect(DesktopIntegration.IsDefaultAsync().GetAwaiter().GetResult(), "roblox:// links open Blox Code");

                    DesktopIntegration.UninstallAsync().GetAwaiter().GetResult();
                    Expect(!File.Exists(Paths.Application) && !File.Exists(DesktopIntegration.DesktopFile) && !File.Exists(DesktopIntegration.IconFile(256)), "files removed");
                    Expect(!MimeApps.Read(DesktopIntegration.MimeAppsFile).ContainsKey("x-scheme-handler/roblox")
                           || Proc.Which("xdg-mime") is not null, "links no longer point at Blox Code");
                }
                finally
                {
                    Environment.SetEnvironmentVariable("APPIMAGE", null);
                }
            });

            if (OperatingSystem.IsLinux())
            {
                Check("helper programs run", () =>
                {
                    var sh = Proc.Which("sh") ?? throw new Exception("no sh on PATH");
                    var result = Proc.RunAsync(sh, new[] { "-c", "echo $LC_ALL; exit 3" }, TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                    Expect(result.ExitCode == 3 && result.Output.Trim() == "C", $"exit {result.ExitCode}, output '{result.Output.Trim()}'");
                    bool timedOut = false;
                    try { Proc.RunAsync(sh, new[] { "-c", "sleep 5" }, TimeSpan.FromMilliseconds(300)).GetAwaiter().GetResult(); }
                    catch (TimeoutException) { timedOut = true; }
                    Expect(timedOut, "timeout kills");
                });
            }
        }
        finally
        {
            try { Directory.Delete(sandbox, recursive: true); } catch { }
        }

        Console.WriteLine(_failures == 0 ? "all checks passed" : $"{_failures} check(s) failed");
        return _failures == 0 ? 0 : 1;
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"ok   {name}");
        }
        catch (Exception ex)
        {
            _failures++;
            Console.WriteLine($"FAIL {name}: {ex.Message}");
        }
    }

    private static void Expect(bool condition, string what)
    {
        if (!condition)
            throw new Exception(what);
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; }
    }
}
