using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace IPTray.Services;

/// <summary>
/// Builds the notification-area icon: the flag of the country the public IP belongs to,
/// falling back to the application globe when the country is unknown or the flag is not
/// available offline.
/// </summary>
internal static class FlagIconFactory
{
    private static readonly string[] SourceTemplates =
    {
        "https://flagcdn.com/w80/{0}.png",
        "https://flagsapi.com/{1}/flat/64.png",
    };

    /// <summary>Notification-area icon edge length for the current display scaling.</summary>
    public static int TraySize
    {
        get
        {
            int size = SystemInformation.SmallIconSize.Width;
            return size is >= 16 and <= 64 ? size : 16;
        }
    }

    /// <summary>
    /// Returns the flag bitmap for an ISO country code, downloading it once and caching it
    /// on disk afterwards. Returns <c>null</c> when it cannot be obtained.
    /// </summary>
    public static async Task<Bitmap?> GetFlagAsync(string countryCode, CancellationToken cancellationToken)
    {
        // The code reaches this method from a remote lookup response, and is used to build both a
        // URL and a cache file name. Only two ASCII letters are ever legitimate.
        if (countryCode.Length != 2 ||
            !char.IsAsciiLetter(countryCode[0]) ||
            !char.IsAsciiLetter(countryCode[1]))
        {
            return null;
        }

        string lower = countryCode.ToLowerInvariant();
        string cacheFile = Path.Combine(AppPaths.FlagCacheDirectory, lower + ".png");

        try
        {
            if (File.Exists(cacheFile))
            {
                Bitmap? cached = Decode(await File.ReadAllBytesAsync(cacheFile, cancellationToken).ConfigureAwait(false));
                if (cached is not null)
                {
                    return cached;
                }

                File.Delete(cacheFile);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CrashReporter.Write(ex);
        }

        foreach (string template in SourceTemplates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(TimeSpan.FromSeconds(9));

            try
            {
                string url = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture, template, lower, countryCode.ToUpperInvariant());

                byte[] bytes = await Web.Client.GetByteArrayAsync(url, attempt.Token).ConfigureAwait(false);
                Bitmap? bitmap = Decode(bytes);
                if (bitmap is null)
                {
                    continue;
                }

                try
                {
                    await File.WriteAllBytesAsync(cacheFile, bytes, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashReporter.Write(ex);
                }

                return bitmap;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Source unreachable - try the next one, then fall back to the globe icon.
            }
        }

        return null;
    }

    /// <summary>
    /// Renders the tray icon. The returned handle owner must be disposed once the icon has
    /// been replaced in the notification area.
    /// </summary>
    public static OwnedIcon CreateTrayIcon(Bitmap? flag, bool offline)
    {
        int size = TraySize;

        using var canvas = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (flag is not null)
            {
                DrawFlag(g, flag, size);
            }
            else
            {
                DrawGlobe(g, size);
            }

            if (offline)
            {
                DrawOfflineBadge(g, size);
            }
        }

        return new OwnedIcon(canvas);
    }

    private static void DrawFlag(Graphics g, Bitmap flag, int size)
    {
        // Fit the flag inside the square, keeping its aspect ratio, and outline it so it stays
        // readable on both light and dark taskbars.
        float scale = Math.Min((float)size / flag.Width, (float)size / flag.Height);
        float width = Math.Max(1f, flag.Width * scale);
        float height = Math.Max(1f, flag.Height * scale);
        var target = new RectangleF((size - width) / 2f, (size - height) / 2f, width, height);

        using (var wrap = new ImageAttributes())
        {
            wrap.SetWrapMode(WrapMode.TileFlipXY);   // avoids the semi-transparent edge column
            g.DrawImage(flag, Rectangle.Round(target), 0, 0, flag.Width, flag.Height, GraphicsUnit.Pixel, wrap);
        }

        using var pen = new Pen(Color.FromArgb(150, 0, 0, 0), 1f);
        var outline = Rectangle.Round(target);
        outline.Width = Math.Max(1, outline.Width - 1);
        outline.Height = Math.Max(1, outline.Height - 1);
        g.SmoothingMode = SmoothingMode.None;
        g.DrawRectangle(pen, outline);
        g.SmoothingMode = SmoothingMode.AntiAlias;
    }

    private static void DrawGlobe(Graphics g, int size)
    {
        using Icon icon = AppIcon.Create(size);
        using Bitmap bitmap = icon.ToBitmap();
        g.DrawImage(bitmap, new Rectangle(0, 0, size, size));
    }

    private static void DrawOfflineBadge(Graphics g, int size)
    {
        float diameter = Math.Max(6f, size * 0.5f);
        var circle = new RectangleF(size - diameter, size - diameter, diameter - 1f, diameter - 1f);

        using var fill = new SolidBrush(Color.FromArgb(220, 38, 38));
        using var edge = new Pen(Color.White, Math.Max(1f, size / 16f));
        g.FillEllipse(fill, circle);
        g.DrawEllipse(edge, circle);
    }

    private static Bitmap? Decode(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var source = new Bitmap(stream);

            // Copy out of the stream-backed bitmap so the buffer can be released.
            var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(copy))
            {
                g.Clear(Color.Transparent);
                g.DrawImageUnscaled(source, 0, 0);
            }

            return copy;
        }
        catch (Exception)
        {
            return null;   // Not an image, or a truncated download.
        }
    }
}

/// <summary>
/// An <see cref="Icon"/> created from a bitmap, paired with the GDI handle behind it.
/// Disposing releases both; <see cref="Icon.FromHandle"/> alone would leak the handle.
/// </summary>
internal sealed class OwnedIcon : IDisposable
{
    private IntPtr _handle;

    public OwnedIcon(Bitmap bitmap)
    {
        _handle = bitmap.GetHicon();
        Icon = Icon.FromHandle(_handle);
    }

    public Icon Icon { get; }

    public void Dispose()
    {
        Icon.Dispose();

        if (_handle != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
