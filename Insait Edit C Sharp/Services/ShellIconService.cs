using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace Insait_Edit_C_Sharp.Services;

/// <summary>
/// Extracts Windows shell icons for file extensions using Win32 SHGetFileInfo API.
/// Converts HICON → raw BGRA pixels via Win32 GDI → SKBitmap → Avalonia Bitmap.
/// No System.Drawing dependency — uses SkiaSharp (already bundled with Avalonia.Skia).
/// Icons are cached by extension for performance.
/// </summary>
public static class ShellIconService
{
    private static readonly ConcurrentDictionary<string, Bitmap?> _iconCache = new();

    #region Win32 Interop

    // SHGetFileInfo constants
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // GetDIBits constants
    private const int BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        byte[]? lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    #endregion

    /// <summary>
    /// Get Windows shell icon for a file extension. Returns null on failure or non-Windows platforms.
    /// </summary>
    public static Bitmap? GetIconForExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return null;

        extension = extension.ToLowerInvariant();
        if (!extension.StartsWith("."))
            extension = "." + extension;

        return _iconCache.GetOrAdd(extension, ext => ExtractIcon(ext));
    }

    /// <summary>
    /// Get Windows shell icon for a specific file path. Falls back to extension-based lookup.
    /// </summary>
    public static Bitmap? GetIconForFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var ext = Path.GetExtension(filePath);
        return GetIconForExtension(ext);
    }

    private static Bitmap? ExtractIcon(string extension)
    {
        try
        {
            var shinfo = new SHFILEINFO();
            var result = SHGetFileInfo(
                extension,
                FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)Marshal.SizeOf(typeof(SHFILEINFO)),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
                return null;

            try
            {
                return HIconToAvaloniaBitmap(shinfo.hIcon);
            }
            finally
            {
                DestroyIcon(shinfo.hIcon);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ShellIconService: Failed to extract icon for '{extension}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convert a Win32 HICON to an Avalonia Bitmap via SkiaSharp.
    /// Path: HICON → GetIconInfo → GetDIBits (raw BGRA) → SKBitmap → PNG → Avalonia Bitmap
    /// </summary>
    private static Bitmap? HIconToAvaloniaBitmap(IntPtr hIcon)
    {
        if (!GetIconInfo(hIcon, out var iconInfo))
            return null;

        IntPtr hdc = IntPtr.Zero;
        try
        {
            // Get bitmap dimensions from the color bitmap
            var bmpHandle = iconInfo.hbmColor != IntPtr.Zero ? iconInfo.hbmColor : iconInfo.hbmMask;
            if (bmpHandle == IntPtr.Zero)
                return null;

            hdc = CreateCompatibleDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
                return null;

            // Query bitmap info (first call with null bits to get dimensions)
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>()
            };

            if (GetDIBits(hdc, bmpHandle, 0, 0, null, ref bmi, DIB_RGB_COLORS) == 0)
                return null;

            int width = bmi.biWidth;
            int height = Math.Abs(bmi.biHeight);
            if (width <= 0 || height <= 0)
                return null;

            // Request 32-bit BGRA, bottom-up
            bmi.biBitCount = 32;
            bmi.biCompression = BI_RGB;
            bmi.biHeight = -height; // negative = top-down DIB (matches SKBitmap row order)
            bmi.biSizeImage = (uint)(width * height * 4);

            var pixelData = new byte[width * height * 4];
            var oldObj = SelectObject(hdc, bmpHandle);

            var scanLines = GetDIBits(hdc, bmpHandle, 0, (uint)height, pixelData, ref bmi, DIB_RGB_COLORS);
            SelectObject(hdc, oldObj);

            if (scanLines == 0)
                return null;

            // If the color bitmap has no alpha channel (all alpha bytes are 0),
            // use the mask bitmap to determine transparency
            if (iconInfo.hbmColor != IntPtr.Zero && !HasAlphaChannel(pixelData))
            {
                ApplyMaskAlpha(hdc, iconInfo.hbmMask, pixelData, width, height);
            }

            // Create SKBitmap from raw BGRA pixels
            using var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var pinnedPixels = skBitmap.GetPixels();
            Marshal.Copy(pixelData, 0, pinnedPixels, pixelData.Length);

            // Encode to PNG and load as Avalonia Bitmap
            using var skImage = SKImage.FromBitmap(skBitmap);
            using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream();
            encoded.SaveTo(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero) DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero) DeleteObject(iconInfo.hbmMask);
            if (hdc != IntPtr.Zero) DeleteDC(hdc);
        }
    }

    /// <summary>
    /// Check if any pixel in the BGRA data has a non-zero alpha value
    /// </summary>
    private static bool HasAlphaChannel(byte[] pixelData)
    {
        for (int i = 3; i < pixelData.Length; i += 4)
        {
            if (pixelData[i] != 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Apply the monochrome mask bitmap as alpha channel.
    /// In the mask: 0 = opaque, 1 = transparent.
    /// </summary>
    private static void ApplyMaskAlpha(IntPtr hdc, IntPtr hbmMask, byte[] pixelData, int width, int height)
    {
        if (hbmMask == IntPtr.Zero)
        {
            // No mask — set all pixels fully opaque
            for (int i = 3; i < pixelData.Length; i += 4)
                pixelData[i] = 255;
            return;
        }

        var maskBmi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
            biSizeImage = (uint)(width * height * 4)
        };

        var maskData = new byte[width * height * 4];
        var oldObj = SelectObject(hdc, hbmMask);
        var result = GetDIBits(hdc, hbmMask, 0, (uint)height, maskData, ref maskBmi, DIB_RGB_COLORS);
        SelectObject(hdc, oldObj);

        if (result == 0)
        {
            // Fallback: set fully opaque
            for (int i = 3; i < pixelData.Length; i += 4)
                pixelData[i] = 255;
            return;
        }

        // Apply mask: mask pixel B channel 0 = opaque (0xFF), non-zero = transparent (0x00)
        for (int i = 0; i < width * height; i++)
        {
            var maskVal = maskData[i * 4]; // B channel of mask (monochrome rendered as 32bpp)
            pixelData[i * 4 + 3] = maskVal == 0 ? (byte)255 : (byte)0;
        }
    }

    /// <summary>
    /// Clear the icon cache (e.g. if system icons change)
    /// </summary>
    public static void ClearCache()
    {
        _iconCache.Clear();
    }
}

