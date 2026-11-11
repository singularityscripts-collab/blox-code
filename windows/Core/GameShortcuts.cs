using System.Net.Http;
using System.Windows.Media.Imaging;

namespace BloxCode.Core;

/// <summary>
/// A desktop shortcut that opens one game through Blox Code (with its mods, flags and the game's
/// own graphics choice), with the game's icon when Roblox has one.
/// </summary>
public static class GameShortcuts
{
    public static string IconsFolder => Path.Combine(Paths.Base, "Icons");

    /// <summary>The launch link a game's shortcut and pinned card use.</summary>
    public static string LaunchLink(long placeId) => $"roblox://experiences/start?placeId={placeId}";

    /// <summary>Creates (or replaces) the shortcut; returns its path.</summary>
    public static async Task<string> CreateAsync(HttpClient http, long placeId, string name, string? iconUrl, string? folder = null)
    {
        string fileName = string.Concat(GameHistory.PlainName(name).Split(Path.GetInvalidFileNameChars())).Trim();
        if (fileName.Length == 0)
            fileName = $"Place {placeId}";
        if (fileName.Length > 80)
            fileName = fileName[..80].Trim();
        string lnk = Path.Combine(folder ?? Paths.Desktop, fileName + ".lnk");

        string? icon = iconUrl is null ? null : await SaveIconAsync(http, placeId, iconUrl);
        // The installed copy when there is one, so the shortcut keeps working after this exe moves.
        string target = File.Exists(Paths.Application) ? Paths.Application : Paths.Process;
        ShellLink.Create(lnk, target, $"-player \"{LaunchLink(placeId)}\"", $"Play {name} with Blox Code", icon);
        Logger.Info("GameShortcuts::Create", $"{lnk} -> place {placeId}");
        return lnk;
    }

    /// <summary>
    /// The game's icon as an .ico (one 256-pixel PNG inside, which Windows Vista and later read);
    /// null if it can't be fetched, so the shortcut falls back to Blox Code's own icon.
    /// </summary>
    public static async Task<string?> SaveIconAsync(HttpClient http, long placeId, string iconUrl)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            byte[] source = await http.GetByteArrayAsync(iconUrl, timeout.Token);
            Directory.CreateDirectory(IconsFolder);
            string path = Path.Combine(IconsFolder, $"{placeId}.ico");
            File.WriteAllBytes(path, Ico(Resize(source, 256)));
            return path;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            Logger.Warn("GameShortcuts::Icon", $"place {placeId}: {ex.Message}");
            return null;
        }
    }

    private static BitmapSource Resize(byte[] image, int size)
    {
        using var input = new MemoryStream(image);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = input;
        bitmap.DecodePixelWidth = size;
        bitmap.DecodePixelHeight = size;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        var bgra = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        bgra.Freeze();
        return bgra;
    }

    /// <summary>
    /// An ICO file with one 32-bit image in the classic layout (a BMP without its file header,
    /// height doubled for the AND mask, rows bottom-up), which every Windows version and toolkit reads.
    /// </summary>
    public static byte[] Ico(BitmapSource image)
    {
        int w = image.PixelWidth, h = image.PixelHeight;
        var pixels = new byte[w * h * 4];
        image.CopyPixels(pixels, w * 4, 0);
        int maskRow = (w + 31) / 32 * 4;
        int imageSize = 40 + pixels.Length + maskRow * h;

        using var stream = new MemoryStream();
        using var o = new BinaryWriter(stream);
        o.Write((ushort)0); o.Write((ushort)1); o.Write((ushort)1);       // ICONDIR: reserved, icon, one image
        o.Write((byte)(w >= 256 ? 0 : w)); o.Write((byte)(h >= 256 ? 0 : h));
        o.Write((byte)0); o.Write((byte)0);                                 // no palette, reserved
        o.Write((ushort)1); o.Write((ushort)32);                            // planes, bits per pixel
        o.Write((uint)imageSize); o.Write((uint)22);                        // size, offset (6 + 16)
        o.Write(40); o.Write(w); o.Write(h * 2);                            // BITMAPINFOHEADER
        o.Write((ushort)1); o.Write((ushort)32);
        o.Write(0); o.Write(pixels.Length + maskRow * h);
        o.Write(0); o.Write(0); o.Write(0); o.Write(0);
        for (int row = h - 1; row >= 0; row--)                              // bottom-up
            o.Write(pixels, row * w * 4, w * 4);
        o.Write(new byte[maskRow * h]);                                     // AND mask: alpha does the work
        o.Flush();
        return stream.ToArray();
    }
}
