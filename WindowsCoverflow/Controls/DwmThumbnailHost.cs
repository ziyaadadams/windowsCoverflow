using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsCoverflow.Controls
{
    internal sealed class DwmThumbnailHost : HwndHost
    {
        public IntPtr SourceHwnd { get; set; }

        /// <summary>
        /// Corner radius in device-independent pixels. Applied via SetWindowRgn on the hosted HWND.
        /// </summary>
        public double CornerRadius { get; set; } = 20;

        /// <summary>
        /// If true, DWM will try to use only the client area (no window frame).
        /// </summary>
        public bool SourceClientAreaOnly { get; set; } = true;

        private IntPtr _hostHwnd;
        private IntPtr _thumb;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _hostHwnd = CreateWindowEx(
                0,
                "static",
                "",
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
                0,
                0,
                1,
                1,
                hwndParent.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (_hostHwnd == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

            TryRegisterThumbnail();
            UpdateThumbnail();

            return new HandleRef(this, _hostHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            if (_thumb != IntPtr.Zero)
            {
                DwmUnregisterThumbnail(_thumb);
                _thumb = IntPtr.Zero;
            }

            if (hwnd.Handle != IntPtr.Zero)
            {
                DestroyWindow(hwnd.Handle);
            }
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);

            if (_hostHwnd != IntPtr.Zero)
            {
                // Convert DIPs to pixels for the hosted HWND.
                var source = PresentationSource.FromVisual(this);
                double scaleX = 1.0;
                double scaleY = 1.0;
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                MoveWindow(
                    _hostHwnd,
                    0,
                    0,
                    Math.Max(1, (int)Math.Round(rcBoundingBox.Width * scaleX)),
                    Math.Max(1, (int)Math.Round(rcBoundingBox.Height * scaleY)),
                    true);

                ApplyRoundedRegion();
                UpdateThumbnail();
            }
        }

        private void TryRegisterThumbnail()
        {
            if (_thumb != IntPtr.Zero || _hostHwnd == IntPtr.Zero || SourceHwnd == IntPtr.Zero)
                return;

            // DWM thumbnail requires composition.
            if (DwmRegisterThumbnail(_hostHwnd, SourceHwnd, out _thumb) != 0)
            {
                _thumb = IntPtr.Zero;
            }
        }

        private void UpdateThumbnail()
        {
            if (_hostHwnd == IntPtr.Zero || SourceHwnd == IntPtr.Zero)
                return;

            if (_thumb == IntPtr.Zero)
                TryRegisterThumbnail();

            if (_thumb == IntPtr.Zero)
                return;

            GetClientRect(_hostHwnd, out RECT rc);

            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION | DWM_TNP_OPACITY
            };

            props.fVisible = true;
            props.opacity = 255;
            props.rcDestination = new RECT { Left = 0, Top = 0, Right = Math.Max(1, rc.Right), Bottom = Math.Max(1, rc.Bottom) };

            if (SourceClientAreaOnly)
                props.dwFlags |= DWM_TNP_SOURCECLIENTAREAONLY;

            props.fSourceClientAreaOnly = SourceClientAreaOnly;

            DwmUpdateThumbnailProperties(_thumb, ref props);
        }

        private void ApplyRoundedRegion()
        {
            if (_hostHwnd == IntPtr.Zero)
                return;

            GetClientRect(_hostHwnd, out RECT rc);
            int w = Math.Max(1, rc.Right - rc.Left);
            int h = Math.Max(1, rc.Bottom - rc.Top);

            // Convert DIP to pixels using the current HwndSource.
            double radiusDip = Math.Max(0, CornerRadius);
            var source = PresentationSource.FromVisual(this);
            double dpiScale = 1.0;
            if (source?.CompositionTarget != null)
                dpiScale = source.CompositionTarget.TransformToDevice.M11;

            int radiusPx = (int)Math.Round(radiusDip * dpiScale);

            if (radiusPx <= 0)
            {
                // Clear region (rect)
                SetWindowRgn(_hostHwnd, IntPtr.Zero, true);
                return;
            }

            IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, radiusPx * 2, radiusPx * 2);
            SetWindowRgn(_hostHwnd, rgn, true);
            // Note: system owns region after SetWindowRgn succeeds.
        }

        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_CLIPCHILDREN = 0x02000000;

        private const int DWM_TNP_RECTDESTINATION = 0x00000001;
        private const int DWM_TNP_VISIBLE = 0x00000008;
        private const int DWM_TNP_OPACITY = 0x00000004;
        private const int DWM_TNP_SOURCECLIENTAREAONLY = 0x00000010;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_THUMBNAIL_PROPERTIES
        {
            public int dwFlags;
            public RECT rcDestination;
            public RECT rcSource;
            public byte opacity;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fVisible;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fSourceClientAreaOnly;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmRegisterThumbnail(IntPtr dest, IntPtr src, out IntPtr thumb);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUnregisterThumbnail(IntPtr thumb);

        [DllImport("dwmapi.dll")]
        private static extern int DwmUpdateThumbnailProperties(IntPtr hThumb, ref DWM_THUMBNAIL_PROPERTIES props);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int exStyle,
            string className,
            string windowName,
            int style,
            int x,
            int y,
            int width,
            int height,
            IntPtr hwndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateRoundRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect, int nWidthEllipse, int nHeightEllipse);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    }
}
