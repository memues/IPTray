using System.Runtime.InteropServices;

namespace IPTray.Services;

internal static partial class NativeMethods
{
    /// <summary>Frees an icon handle produced by <see cref="System.Drawing.Bitmap.GetHicon"/>.</summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int DestroyIcon(IntPtr hIcon);
}
