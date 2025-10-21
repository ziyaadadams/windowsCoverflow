using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using WindowsCoverflow.Models;

namespace WindowsCoverflow.Services
{
    public class WindowManager
    {
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
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("shell32.dll")]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

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

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;
        private const uint WM_CLOSE = 0x0010;
        private const int DWMWA_CLOAKED = 14;
        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_SMALLICON = 0x1;

        #endregion

        public List<WindowInfo> GetWindows()
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
                    Thumbnail = CaptureWindow(hWnd),
                    Icon = GetWindowIcon(hWnd, processName)
                };

                windows.Add(windowInfo);
                return true;
            }, IntPtr.Zero);

            return windows;
        }

        private BitmapSource? CaptureWindow(IntPtr hWnd)
        {
            try
            {
                GetWindowRect(hWnd, out RECT rect);
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                if (width <= 0 || height <= 0)
                    return null;

                // Limit size for performance
                int maxWidth = 400;
                int maxHeight = 300;
                
                if (width > maxWidth || height > maxHeight)
                {
                    double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
                    width = (int)(width * scale);
                    height = (int)(height * scale);
                }

                using var bmp = new System.Drawing.Bitmap(width, height);
                using var graphics = System.Drawing.Graphics.FromImage(bmp);
                var hdc = graphics.GetHdc();
                
                try
                {
                    PrintWindow(hWnd, hdc, 0);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }

                var hBitmap = bmp.GetHbitmap();
                try
                {
                    return Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    DeleteObject(hBitmap);
                }
            }
            catch
            {
                return null;
            }
        }

        private BitmapSource? GetWindowIcon(IntPtr hWnd, string processName)
        {
            try
            {
                // Try to get icon from process
                var process = Process.GetProcessesByName(processName).FirstOrDefault();
                if (process != null)
                {
                    var shinfo = new SHFILEINFO();
                    SHGetFileInfo(process.MainModule?.FileName ?? string.Empty, 0, ref shinfo, 
                        (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_SMALLICON);

                    if (shinfo.hIcon != IntPtr.Zero)
                    {
                        var icon = System.Drawing.Icon.FromHandle(shinfo.hIcon);
                        var bitmap = icon.ToBitmap();
                        var hBitmap = bitmap.GetHbitmap();
                        
                        try
                        {
                            return Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                            bitmap.Dispose();
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        public void SwitchToWindow(IntPtr hWnd)
        {
            // Restore if minimized
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }
            
            // Bring to foreground
            SetForegroundWindow(hWnd);
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
    }
}
