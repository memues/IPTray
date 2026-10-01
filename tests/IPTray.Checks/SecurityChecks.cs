using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Reflection;
using IPTray.Forms;
using IPTray.Services;

internal static class SecurityChecks
{
    public static void Run()
    {
        var failures = new List<Exception>();
        foreach (Action check in new Action[] { ClipboardExport, CsvRoundTrips, FlagDecoding, CachedFlags })
        {
            try { check(); }
            catch (Exception ex) { failures.Add(ex); }
        }

        if (failures.Count > 0) throw new AggregateException(failures);
        Console.WriteLine("Clipboard/CSV export and bounded flag/cache decoding checks passed.");
    }

    private static void ClipboardExport()
    {
        var ordinary = new ListViewItem(new[]
            { "2026-10-01 12:00:00", "192.0.2.1", "Example (EX)", "Example ISP, Inc." });
        Require(LogsForm.BuildClipboardText(new[] { ordinary }) ==
            "2026-10-01 12:00:00\t192.0.2.1\tExample (EX)\tExample ISP, Inc." + Environment.NewLine,
            "Normal copied rows must remain unchanged.");
        foreach (string value in new[] { "=1+1", "+1+1", "-1+1", "@SUM(1,1)", "  =1+1",
            "＝1+1", "＋1+1", "－1+1", "＠SUM(1,1)", "\"Quoted ISP\"", "'quoted ISP" })
        {
            var item = new ListViewItem(new[] { "timestamp", "192.0.2.1", "Country", value });
            string copied = LogsForm.BuildClipboardText(new[] { item });
            Require(copied.Split('\t')[3].StartsWith('\''),
                "Copy must guard formula-like remote metadata: " + value);
        }

        var injected = new ListViewItem(new[] { "timestamp", "192.0.2.1", "Country", "ISP\t=1+1\r\n=2+2" });
        string result = LogsForm.BuildClipboardText(new[] { injected });
        Require(result.TrimEnd('\r', '\n').Split('\t').Length == 4 &&
            result.Count(c => c == '\n') == 1 && result.Contains("ISP =1+1  =2+2", StringComparison.Ordinal),
            "Remote metadata must not inject clipboard columns or rows.");
        Require(LogsForm.BuildClipboardText(Array.Empty<ListViewItem>()) == string.Empty,
            "An empty history must copy nothing.");
    }

    private static void CsvRoundTrips()
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var escape = typeof(IpLogStore).GetMethod("Escape", flags)!;
        var unescape = typeof(IpLogStore).GetMethod("Unescape", flags)!;
        var parse = typeof(IpLogStore).GetMethod("ParseCsvLine", flags)!;
        foreach (string value in new[] { "Example ISP", "ISP, Inc.", "Quoted \"ISP\"", "'=1+1",
            "=1+1", "+1+1", "-1+1", "@SUM(1,1)", "＝1+1", "＋1+1", "－1+1", "＠SUM(1,1)" })
        {
            string encoded = (string)escape.Invoke(null, new object[] { value })!;
            string[] cells = (string[])parse.Invoke(null, new object[] { encoded })!;
            Require(cells.Length == 1 &&
                (string)unescape.Invoke(null, new object[] { cells[0] })! == value,
                "CSV guards must preserve log display values and column boundaries.");
            if ("=+-@＝＋－＠".Contains(value[0]))
                Require(cells[0].StartsWith('\''), "CSV export must guard formula-like metadata.");
        }
    }

    private static void FlagDecoding()
    {
        using Bitmap? normal = Decode(Png(80, 48));
        Require(normal?.Size == new Size(80, 48), "A normal provider flag must still decode.");
        using Bitmap? square = Decode(Png(64, 64));
        Require(square?.Size == new Size(64, 64), "The fallback provider flag must still decode.");
        using Bitmap? boundary = Decode(Png(256, 256));
        Require(boundary?.Size == new Size(256, 256), "Allow flags at the dimension limit.");

        // A small, valid compressed PNG proves the encoded-byte cap alone is insufficient.
        byte[] oversized = Png(1024, 1024);
        Require(oversized.Length < 2 * 1024 * 1024, "The regression fixture must fit the HTTP byte cap.");
        using Bitmap? rejected = Decode(oversized);
        Require(rejected is null, "Reject oversized decoded flag dimensions before allocating the image.");
        byte[] valid = Png(80, 48);
        foreach ((uint width, uint height) in new[] { (0u, 48u), (80u, 0u), (257u, 48u), (80u, 257u),
            (uint.MaxValue, uint.MaxValue) })
        {
            byte[] header = (byte[])valid.Clone();
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), width);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20, 4), height);
            Require(Decode(header) is null, "Reject zero, overflowing or oversized PNG dimensions.");
        }

        byte[] tooManyBytes = new byte[2 * 1024 * 1024 + 1];
        valid.CopyTo(tooManyBytes, 0);
        Require(Decode(tooManyBytes) is null, "Reject oversized encoded images even with valid dimensions.");
        using var otherFormat = new MemoryStream();
        using (var bitmap = new Bitmap(64, 64)) bitmap.Save(otherFormat, ImageFormat.Bmp);
        Require(Decode(otherFormat.ToArray()) is null, "Only the expected PNG format may reach the decoder.");
        Require(Decode(valid[..32]) is null && Decode(valid[..33]) is null,
            "Reject a truncated PNG header or image without leaking a bitmap.");
        Require(Decode(Array.Empty<byte>()) is null && Decode(new byte[] { 1, 2, 3 }) is null,
            "Invalid images must retain the globe/fallback behavior.");
    }

    private static void CachedFlags()
    {
        string path = Path.Combine(Path.GetTempPath(), "IPTray-security-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, Png(80, 48));
            using Bitmap? cached = FlagIconFactory.ReadCachedFlagAsync(path, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(cached?.Size == new Size(80, 48), "Normal cached flags must still load.");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write))
                stream.SetLength(2 * 1024 * 1024 + 1);
            using Bitmap? oversized = FlagIconFactory.ReadCachedFlagAsync(path, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(oversized is null, "A cached file must obey the HTTP encoded-byte limit too.");
            File.WriteAllBytes(path, Png(1024, 1024));
            using Bitmap? expanded = FlagIconFactory.ReadCachedFlagAsync(path, CancellationToken.None)
                .GetAwaiter().GetResult();
            Require(expanded is null, "Cached flags must obey decoded dimension limits.");
        }
        finally { File.Delete(path); }
    }

    private static Bitmap? Decode(byte[] bytes) => (Bitmap?)typeof(FlagIconFactory)
        .GetMethod("Decode", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { bytes });

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new Bitmap(width, height);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
