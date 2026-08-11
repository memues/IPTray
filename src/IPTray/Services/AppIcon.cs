using System.Drawing;
using System.Reflection;

namespace IPTray.Services;

/// <summary>Access to the embedded application icon.</summary>
internal static class AppIcon
{
    private const string ResourceName = "IPTray.Resources.app.ico";

    private static readonly byte[] Data = ReadResource();

    /// <summary>Creates the icon at its natural size. The caller owns the result.</summary>
    public static Icon Create()
    {
        using var stream = new MemoryStream(Data, writable: false);
        return new Icon(stream);
    }

    /// <summary>Creates the icon at the requested size. The caller owns the result.</summary>
    public static Icon Create(int size)
    {
        using var stream = new MemoryStream(Data, writable: false);
        return new Icon(stream, size, size);
    }

    private static byte[] ReadResource()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
