using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Collections.Concurrent;
using WindowsCoverflow.Models;

namespace WindowsCoverflow.Services
{
    public class WindowManager
    {
        private static readonly ConcurrentDictionary<IntPtr, BitmapSource> ThumbnailCache = new();
        #region Win32 API

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
            IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);
        
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
        
        [DllImport("dwmapi.dll")]
        private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);
        
        [DllImport("dwmapi.dll")]
        private static extern int DwmUnregisterThumbnail(IntPtr thumb);
        
        [DllImport("dwmapi.dll")]
        private static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);
        
        [DllImport("dwmapi.dll")]
        private static extern int DwmQueryThumbnailSourceSize(IntPtr thumb, out SIZE size);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
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
        private struct SIZE
        {
            public int cx;
            public int cy;
        }
        
        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            public bool fVisible;
            public bool fSourceClientAreaOnly;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint WM_CLOSE = 0x0010;
        private const int DWMWA_CLOAKED = 14;
        private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_SMALLICON = 0x1;
        private const uint SHGFI_SYSICONINDEX = 0x4000;
        private const int SHIL_JUMBO = 0x4;
        private const int ILD_TRANSPARENT = 0x1;

        [ComImport]
        [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IImageList
        {
            int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
            int ReplaceIcon(int i, IntPtr hicon, out int pi);
            int SetOverlayImage(int iImage, int iOverlay);
            int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
            int AddMasked(IntPtr hbmImage, int crMask, out int pi);
            int Draw(ref IMAGELISTDRAWPARAMS pimldp);
            int Remove(int i);
            int GetIcon(int i, int flags, out IntPtr picon);
            // Remaining methods not needed
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IMAGELISTDRAWPARAMS
        {
            public int cbSize;
            public IntPtr himl;
            public int i;
            public IntPtr hdcDst;
            public int x;
            public int y;
            public int cx;
            public int cy;
            public int xBitmap;
            public int yBitmap;
            public int rgbBk;
            public int rgbFg;
            public int fStyle;
            public int dwRop;
            public int fState;
            public int Frame;
            public int crEffect;
        }

        #endregion

        public List<WindowInfo> GetWindows(bool captureThumbnails = false)
        {
            var windows = new List<WindowInfo>();
            var shellWindow = GetShellWindow();

            EnumWindows((hWnd, lParam) =>
            {
                // Skip invisible windows
                if (!IsWindowVisible(hWnd))
                    return true;

                // Skip shell window
                if (hWnd == shellWindow)
                    return true;

                // Skip windows with no title
                int length = GetWindowTextLength(hWnd);
                if (length == 0)
                    return true;

                // Get window title
                var title = new StringBuilder(length + 1);
                GetWindowText(hWnd, title, title.Capacity);
                
                if (string.IsNullOrWhiteSpace(title.ToString()))
                    return true;

                // Skip tool windows
                var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
                if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                    return true;

                // Check if window is cloaked (hidden by Windows)
                bool isCloaked = false;
                try
                {
                    DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out isCloaked, Marshal.SizeOf(typeof(bool)));
                    if (isCloaked)
                        return true;
                }
                catch { }

                // Get process info
                GetWindowThreadProcessId(hWnd, out uint processId);
                string processName = string.Empty;
                
                try
                {
                    var process = Process.GetProcessById((int)processId);
                    processName = process.ProcessName;
                }
                catch { }

                // Create window info
                var windowInfo = new WindowInfo
                {
                    Handle = hWnd,
                    Title = title.ToString(),
                    ProcessName = processName,
                    IsMinimized = IsIconic(hWnd),
                    Thumbnail = captureThumbnails
                        ? CaptureWindow(hWnd, allowScreenCapture: true)
                        : (ThumbnailCache.TryGetValue(hWnd, out var cached) ? cached : null),
                    Icon = GetWindowIcon(hWnd, processName)
                };

                windows.Add(windowInfo);
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        public BitmapSource? CaptureThumbnail(IntPtr hWnd, bool allowScreenCapture)
        {
            return CaptureWindow(hWnd, allowScreenCapture);
        }

        private static bool IsLikelyBlankCapture(BitmapSource bitmap)
        {
            try
            {
                if (bitmap.PixelWidth < 2 || bitmap.PixelHeight < 2)
                    return true;

                BitmapSource src = bitmap;
                if (src.Format != System.Windows.Media.PixelFormats.Bgra32 &&
                    src.Format != System.Windows.Media.PixelFormats.Pbgra32)
                {
                    var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                        src,
                        System.Windows.Media.PixelFormats.Bgra32,
                        null,
                        0);
                    converted.Freeze();
                    src = converted;
                }

                int w = src.PixelWidth;
                int h = src.PixelHeight;

                var samplePoints = new (int x, int y)[]
                {
                    (1, 1),
                    (w / 2, 1),
                    (w - 2, 1),
                    (1, h / 2),
                    (w / 2, h / 2),
                    (w - 2, h / 2),
                    (1, h - 2),
                    (w / 2, h - 2),
                    (w - 2, h - 2)
                };

                int minLuma = 255;
                int maxLuma = 0;

                byte[] pixel = new byte[4];
                foreach (var (x, y) in samplePoints)
                {
                    src.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
                    // BGRA
                    int b = pixel[0];
                    int g = pixel[1];
                    int r = pixel[2];
                    int luma = (r + g + b) / 3;
                    if (luma < minLuma) minLuma = luma;
                    if (luma > maxLuma) maxLuma = luma;
                }

                // Nearly uniform image (all black-ish or all white-ish) is a common "failed capture" symptom.
                if (maxLuma - minLuma <= 8)
                {
                    if (maxLuma <= 8) return true;      // black
                    if (minLuma >= 247) return true;    // white
                }

                return false;
            }
            catch
            {
                // If we can't inspect pixels, assume it's not blank so we still attempt to render it.
                return false;
            }
        }

        private BitmapSource? CaptureWindow(IntPtr hWnd, bool allowScreenCapture)
        {
            try
            {
                // If minimized, prefer last known good cached thumbnail
                if (IsIconic(hWnd) && ThumbnailCache.TryGetValue(hWnd, out var cached))
                    return cached;
                
                // Get window position on screen (includes borders/shadows)
                if (!GetWindowRect(hWnd, out RECT windowRect))
                {
                    Debug.WriteLine("Failed to get window rect");
                    return null;
                }
                
                // Get actual content bounds (excludes borders/shadows)
                RECT contentRect = windowRect;
                if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT extendedRect, Marshal.SizeOf(typeof(RECT))) == 0)
                {
                    contentRect = extendedRect;
                }
                
                int width = contentRect.Right - contentRect.Left;
                int height = contentRect.Bottom - contentRect.Top;

                if (width <= 10 || height <= 10 || width > 10000 || height > 10000)
                {
                    Debug.WriteLine($"Invalid window size: {width}x{height}");
                    return null;
                }

                // Limit size for performance but prioritize quality
                int maxWidth = 2560;
                int maxHeight = 1600;
                
                double scaleX = 1.0;
                double scaleY = 1.0;
                
                if (width > maxWidth)
                    scaleX = (double)maxWidth / width;
                
                if (height > maxHeight)
                    scaleY = (double)maxHeight / height;
                
                double scale = Math.Min(scaleX, scaleY);
                int targetWidth = (int)(width * scale);
                int targetHeight = (int)(height * scale);

                // METHOD 0: Windows Graphics Capture (best for modern GPU/UWP/Chromium windows)
                try
                {
                    var wgc = WindowsGraphicsCaptureService.TryCaptureWindow(hWnd, maxWidth, maxHeight);
                    if (wgc != null && !IsLikelyBlankCapture(wgc))
                    {
                        // Crop to remove borders if WGC captured the full window
                        var cropped = CropToBounds(wgc, windowRect, contentRect);
                        ThumbnailCache[hWnd] = cropped;
                        return cropped;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WGC capture exception: {ex.Message}");
                }

                // METHOD 1: Try PrintWindow (doesn't include our overlay)
                using var bitmap = new System.Drawing.Bitmap(width, height);
                using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                
                graphics.Clear(System.Drawing.Color.White);
                IntPtr hdcBitmap = graphics.GetHdc();
                
                try
                {
                    // Try multiple PrintWindow flags
                    bool success = PrintWindow(hWnd, hdcBitmap, 2); // PW_RENDERFULLCONTENT
                    
                    if (!success)
                        success = PrintWindow(hWnd, hdcBitmap, 0);
                }
                finally
                {
                    graphics.ReleaseHdc(hdcBitmap);
                }

                // Scale
                System.Drawing.Bitmap finalBmp = bitmap;
                if (scale < 0.99)
                {
                    finalBmp = new System.Drawing.Bitmap(targetWidth, targetHeight);
                    using var g = System.Drawing.Graphics.FromImage(finalBmp);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(bitmap, 0, 0, targetWidth, targetHeight);
                }

                BitmapSource? printWindowSource = null;
                var hBmp = finalBmp.GetHbitmap();
                try
                {
                    var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                        hBmp,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    
                    bitmapSource.Freeze();
                    Debug.WriteLine($"Captured via PrintWindow: {width}x{height}");

                    if (!IsLikelyBlankCapture(bitmapSource))
                    {
                        printWindowSource = bitmapSource;
                    }
                    else
                    {
                        Debug.WriteLine("PrintWindow capture looked blank; will try screen capture if allowed");
                    }
                }
                finally
                {
                    DeleteObject(hBmp);
                    if (finalBmp != bitmap)
                        finalBmp.Dispose();
                }

                if (printWindowSource != null)
                {
                    ThumbnailCache[hWnd] = printWindowSource;
                    return printWindowSource;
                }

                // METHOD 2: Optional screen capture (fast + works for many GPU windows, but can capture our UI overlay)
                if (!allowScreenCapture)
                    return null;

                try
                {
                    using var screenBitmap = new System.Drawing.Bitmap(width, height);
                    using var g = System.Drawing.Graphics.FromImage(screenBitmap);

                    g.CopyFromScreen(contentRect.Left, contentRect.Top, 0, 0, new System.Drawing.Size(width, height));

                    System.Drawing.Bitmap finalBitmap = screenBitmap;
                    if (scale < 0.99)
                    {
                        finalBitmap = new System.Drawing.Bitmap(targetWidth, targetHeight);
                        using var gScale = System.Drawing.Graphics.FromImage(finalBitmap);
                        gScale.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        gScale.DrawImage(screenBitmap, 0, 0, targetWidth, targetHeight);
                    }

                    var hBitmap = finalBitmap.GetHbitmap();
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        bitmapSource.Freeze();
                        Debug.WriteLine($"Captured via screen capture: {width}x{height}");

                        if (IsLikelyBlankCapture(bitmapSource))
                        {
                            Debug.WriteLine("Screen capture looked blank; falling back to text card");
                            return null;
                        }

                        return bitmapSource;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                        if (finalBitmap != screenBitmap)
                            finalBitmap.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Screen capture failed: {ex.Message}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error capturing window: {ex.Message}");
                return null;
            }
        }

        private BitmapSource? GetWindowIcon(IntPtr hWnd, string processName)
        {
            try
            {
                string? path = null;
                try
                {
                    var process = Process.GetProcessesByName(processName).FirstOrDefault();
                    path = process?.MainModule?.FileName;
                }
                catch { }

                // 1) Try to fetch a high-res shell icon (256px) for the process path.
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var shfi = new SHFILEINFO();
                    var res = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_SYSICONINDEX);
                    if (res != IntPtr.Zero)
                    {
                        var iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                        if (SHGetImageList(SHIL_JUMBO, ref iid, out var ppv) == 0 && ppv != IntPtr.Zero)
                        {
                            var imageList = (IImageList)Marshal.GetObjectForIUnknown(ppv);
                            try
                            {
                                if (imageList.GetIcon(shfi.iIcon, ILD_TRANSPARENT, out var hIcon) == 0 && hIcon != IntPtr.Zero)
                                {
                                    try
                                    {
                                        var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                        src.Freeze();
                                        return src;
                                    }
                                    finally
                                    {
                                        DestroyIcon(hIcon);
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.Release(ppv);
                            }
                        }
                    }

                    // 2) Fall back to a smaller file icon.
                    shfi = new SHFILEINFO();
                    res = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_SMALLICON);
                    if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;

                    try
                    {
                        var iconSrc = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        iconSrc.Freeze();
                        return iconSrc;
                    }
                    finally
                    {
                        DestroyIcon(shfi.hIcon);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        public void SwitchToWindow(IntPtr hWnd)
        {
            try
            {
                // Get current foreground window thread
                IntPtr foregroundWindow = GetForegroundWindow();
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out _);
                uint currentThreadId = GetCurrentThreadId();
                
                // Attach to the foreground window thread to bypass restrictions
                if (foregroundThreadId != currentThreadId)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }
                
                // Restore if minimized
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                
                // Force the window to the top
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                SetFocus(hWnd);
                
                // Detach threads
                if (foregroundThreadId != currentThreadId)
                {
                    AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
                
                Debug.WriteLine($"Successfully switched to window: {hWnd}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SwitchToWindow: {ex.Message}");
            }
        }

        public void CloseWindow(IntPtr hWnd)
        {
            SendMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        public void ShowDesktop()
        {
            // Minimize all windows
            var shellWindow = GetShellWindow();
            SendMessage(shellWindow, 0x111, (IntPtr)0x7502, IntPtr.Zero);
        }

        private BitmapSource CropToBounds(BitmapSource source, RECT windowRect, RECT contentRect)
        {
            try
            {
                // Calculate crop region
                int cropLeft = contentRect.Left - windowRect.Left;
                int cropTop = contentRect.Top - windowRect.Top;
                int cropWidth = contentRect.Right - contentRect.Left;
                int cropHeight = contentRect.Bottom - contentRect.Top;

                // Ensure crop region is valid
                if (cropLeft < 0) cropLeft = 0;
                if (cropTop < 0) cropTop = 0;
                if (cropLeft + cropWidth > source.PixelWidth) cropWidth = source.PixelWidth - cropLeft;
                if (cropTop + cropHeight > source.PixelHeight) cropHeight = source.PixelHeight - cropTop;

                if (cropWidth <= 0 || cropHeight <= 0)
                    return source; // Can't crop, return original

                // Create cropped bitmap
                var cropped = new CroppedBitmap(source, new Int32Rect(cropLeft, cropTop, cropWidth, cropHeight));
                cropped.Freeze();
                return cropped;
            }
            catch
            {
                return source; // If cropping fails, return original
            }
        }
    }
}
