using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Interop;
using WindowsCoverflow.Controls;
using WindowsCoverflow.Services;
using WindowsCoverflow.Models;

namespace WindowsCoverflow
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public uint cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private const double CardMeshWidth = 400;
        private const double CardMeshHeight = 300;

        public static readonly DependencyProperty CoverflowIndexProperty =
            DependencyProperty.RegisterAttached(
                "CoverflowIndex",
                typeof(int),
                typeof(MainWindow),
                new PropertyMetadata(-1));

        public static void SetCoverflowIndex(DependencyObject element, int value)
            => element.SetValue(CoverflowIndexProperty, value);

        public static int GetCoverflowIndex(DependencyObject element)
            => (int)element.GetValue(CoverflowIndexProperty);

        private readonly WindowManager _windowManager;
        private readonly KeyboardHook _keyboardHook;
        private readonly SystemTrayService _trayService;
        
        private List<WindowInfo> _windows = new();
        private int _currentIndex = 0;
        private bool _isVisible = false;

        private readonly Dictionary<int, GeometryModel3D> _modelsByIndex = new();
        private readonly Dictionary<ModelVisual3D, TransformParts> _transformPartsByVisual = new();
        private readonly Dictionary<int, FrameworkElement> _liveCardByIndex = new();
        private readonly Dictionary<FrameworkElement, Transform2DParts> _transform2dPartsByElement = new();
        private System.Threading.CancellationTokenSource? _thumbCts;

        // Prefer DWM thumbnails for live, high-quality previews while the switcher is open.
        // Keep this runtime-configurable to avoid compile-time unreachable branches.
        private bool _useLiveDwmThumbnails = true;

        private sealed class TransformParts
        {
            public required ScaleTransform3D Scale { get; init; }
            public required RotateTransform3D Rotate { get; init; }
            public required AxisAngleRotation3D AxisAngle { get; init; }
            public required TranslateTransform3D Translate { get; init; }
        }

        private sealed class Transform2DParts
        {
            public required ScaleTransform Scale { get; init; }
            public required SkewTransform Skew { get; init; }
            public required TranslateTransform Translate { get; init; }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Enable DWM glass/blur behind the switcher background (best-effort; silently fails if unsupported)
            // NOTE: DWM thumbnails won't render into layered windows (WPF AllowsTransparency=True).
            this.SourceInitialized += (_, __) =>
            {
                try { EnableDwmEffects(); } catch { }
            };
            
            // Start hidden immediately
            this.Visibility = Visibility.Hidden;
            this.WindowState = WindowState.Minimized;
            
            _windowManager = new WindowManager();
            _keyboardHook = new KeyboardHook();
            _trayService = new SystemTrayService();

            // Subscribe to keyboard events
            _keyboardHook.AltTabPressed += OnAltTabPressed;
            _keyboardHook.AltReleased += OnAltReleased;
            _keyboardHook.Register();

            // Subscribe to tray events
            _trayService.ExitRequested += (s, e) => Application.Current.Shutdown();
            _trayService.ShowSwitcherRequested += (s, e) => ShowSwitcher();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public int dwFlags;
            public bool fEnable;
            public IntPtr hRgnBlur;
            public bool fTransitionOnMaximized;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int cxLeftWidth;
            public int cxRightWidth;
            public int cyTopHeight;
            public int cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        private void EnableDwmEffects()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            // Extend the DWM frame into the entire client area ("glass"), enabling transparency
            // without making the window layered (required for DWM thumbnails).
            var margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            DwmExtendFrameIntoClientArea(hwnd, ref margins);

            if (PresentationSource.FromVisual(this) is HwndSource source && source.CompositionTarget != null)
            {
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            }

            var bb = new DWM_BLURBEHIND
            {
                dwFlags = 1, // DWM_BB_ENABLE
                fEnable = true,
                hRgnBlur = IntPtr.Zero,
                fTransitionOnMaximized = false
            };

            DwmEnableBlurBehindWindow(hwnd, ref bb);
        }

        private void OnAltTabPressed(object? sender, KeyboardHook.AltTabEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnAltTabPressed called");
            
            // Do NOT block the keyboard hook thread; schedule work on UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (!_isVisible)
                {
                    ShowSwitcher();
                }
                else
                {
                    if (e.Reverse)
                        NavigatePrevious();
                    else
                        NavigateNext();
                }
            });
        }

        private void OnAltReleased(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnAltReleased called");
            
            // Do NOT block the keyboard hook thread; schedule work on UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (_isVisible)
                {
                    SelectWindow();
                }
            });
        }

        private void ShowSwitcher()
        {
            System.Diagnostics.Debug.WriteLine("ShowSwitcher called");
            
            // Get all windows
            _windows = _windowManager.GetWindows(captureThumbnails: false);
            
            System.Diagnostics.Debug.WriteLine($"Found {_windows.Count} windows");
            
            // Log thumbnail status
            int withThumbs = _windows.Count(w => w.Thumbnail != null);
            int withoutThumbs = _windows.Count - withThumbs;
            System.Diagnostics.Debug.WriteLine($"Windows WITH thumbnails: {withThumbs}");
            System.Diagnostics.Debug.WriteLine($"Windows WITHOUT thumbnails: {withoutThumbs}");
            
            foreach (var w in _windows)
            {
                System.Diagnostics.Debug.WriteLine($"  - {w.Title}: Thumbnail={(w.Thumbnail != null ? "YES" : "NO")}");
            }
            
            if (_windows.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("No windows found, hiding switcher");
                return;
            }

            _currentIndex = 0;
            _isVisible = true;

            // Ensure fully visible when shown
            this.Opacity = 1.0;

            // Preload a few thumbnails while we're still hidden to avoid capturing the switcher overlay
            PreloadThumbnailsAroundCurrent();

            // Position window on the monitor with the mouse cursor
            PositionOnCurrentMonitor();

            // Restore window state and show
            this.WindowState = WindowState.Maximized;
            this.Visibility = Visibility.Visible;
            this.Activate();
            this.Focus();
            
            System.Diagnostics.Debug.WriteLine("Window shown, building coverflow");

            // Build 3D coverflow
            BuildCoverflow();
            UpdateCurrentWindow();

            // Lightweight: load only a few thumbnails around the selection
            QueueThumbnailLoadAroundCurrent();
            
            System.Diagnostics.Debug.WriteLine("Coverflow built successfully");
        }

        private void PositionOnCurrentMonitor()
        {
            // Get cursor position
            if (GetCursorPos(out POINT cursorPos))
            {
                // Get the monitor that contains the cursor
                IntPtr hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
                
                if (hMonitor != IntPtr.Zero)
                {
                    MONITORINFO monitorInfo = new MONITORINFO();
                    monitorInfo.cbSize = (uint)Marshal.SizeOf(typeof(MONITORINFO));
                    
                    if (GetMonitorInfo(hMonitor, ref monitorInfo))
                    {
                        // Set window position to be on the target monitor
                        // When we maximize, it will maximize on whichever monitor contains the window's position
                        this.Left = monitorInfo.rcMonitor.Left + 100;
                        this.Top = monitorInfo.rcMonitor.Top + 100;
                    }
                }
            }
        }

        private void HideSwitcher()
        {
            _isVisible = false;

            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            _thumbCts = null;
            
            // Reset keyboard hook state to release Alt key
            _keyboardHook.ResetState();

            // Cleanup live DWM thumbnails
            if (LiveCanvas != null)
            {
                foreach (var root in LiveCanvas.Children.OfType<Grid>())
                {
                    foreach (var host in root.Children.OfType<DwmThumbnailHost>())
                    {
                        host.Dispose();
                    }
                }

                LiveCanvas.Children.Clear();
                LiveCanvas.Visibility = Visibility.Collapsed;
            }
            _liveCardByIndex.Clear();
            _transform2dPartsByElement.Clear();

            if (Viewport != null)
            {
                Viewport.Visibility = Visibility.Visible;
            }

            // Make it disappear instantly to avoid any ghosting artifacts
            this.Opacity = 0.0;
            
            this.Visibility = Visibility.Hidden;
            this.WindowState = WindowState.Minimized;
            
            // Clear only dynamic window cards (keep lights so rendering doesn't go black)
            ResetViewportToLightsOnly();
            
            System.Diagnostics.Debug.WriteLine("Switcher hidden and keyboard state reset");
        }

        private void ResetViewportToLightsOnly()
        {
            if (Viewport == null)
                return;

            Viewport.Children.Clear();

            if (LightsVisual != null)
            {
                Viewport.Children.Add(LightsVisual);
            }
        }

        private void BuildCoverflow()
        {
            if (_useLiveDwmThumbnails)
            {
                BuildCoverflowLive();
                return;
            }

            ResetViewportToLightsOnly();

            _modelsByIndex.Clear();
            _transformPartsByVisual.Clear();

            for (int i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                var visual = CreateWindowVisual(window, i);
                Viewport.Children.Add(visual);
            }

            AnimateCoverflow();
        }

        private void BuildCoverflowLive()
        {
            if (Viewport != null)
                Viewport.Visibility = Visibility.Collapsed;

            if (LiveCanvas == null)
            {
                // Fallback to legacy renderer if XAML layer isn't available.
                _useLiveDwmThumbnails = false;
                BuildCoverflow();
                return;
            }

            LiveCanvas.Visibility = Visibility.Visible;

            foreach (var root in LiveCanvas.Children.OfType<Grid>())
            {
                foreach (var host in root.Children.OfType<DwmThumbnailHost>())
                {
                    host.Dispose();
                }
            }

            LiveCanvas.Children.Clear();
            _liveCardByIndex.Clear();
            _transform2dPartsByElement.Clear();

            // Card size in 2D (kept close to the existing 3D mesh proportions)
            const double cardW = 520;
            const double cardH = 390;
            const double footerH = 80;
            const double previewH = cardH - footerH;

            for (int i = 0; i < _windows.Count; i++)
            {
                var w = _windows[i];

                var root = new Grid
                {
                    Width = cardW,
                    Height = cardH,
                    IsHitTestVisible = false,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetBitmapScalingMode(root, BitmapScalingMode.HighQuality);

                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(previewH) });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(footerH) });

                var previewHost = new DwmThumbnailHost
                {
                    SourceHwnd = w.Handle,
                    CornerRadius = 20,
                    SourceClientAreaOnly = true
                };
                Grid.SetRow(previewHost, 0);
                root.Children.Add(previewHost);

                var footer = new Grid { Margin = new Thickness(16, 0, 16, 0) };
                footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(footer, 1);

                var icon = new Image
                {
                    Width = 36,
                    Height = 36,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    Source = w.Icon
                };
                footer.Children.Add(icon);

                var title = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    FontSize = 18,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Text = string.IsNullOrWhiteSpace(w.Title) ? "(untitled)" : w.Title
                };
                Grid.SetColumn(title, 1);
                footer.Children.Add(title);

                root.Children.Add(footer);

                // Rounded corners for the overall card silhouette.
                // Note: HwndHost itself doesn't clip in WPF; the preview rounding is handled by DwmThumbnailHost via SetWindowRgn.
                root.Clip = new RectangleGeometry(new Rect(0, 0, cardW, cardH), 20, 20);

                // Transform setup
                var scale = new ScaleTransform(1, 1);
                var skew = new SkewTransform(0, 0);
                var translate = new TranslateTransform(0, 0);
                var tg = new TransformGroup();
                tg.Children.Add(scale);
                tg.Children.Add(skew);
                tg.Children.Add(translate);
                root.RenderTransform = tg;
                root.RenderTransformOrigin = new Point(0.5, 0.5);

                _transform2dPartsByElement[root] = new Transform2DParts { Scale = scale, Skew = skew, Translate = translate };
                _liveCardByIndex[i] = root;

                LiveCanvas.Children.Add(root);
            }

            // Ensure layout is available for initial positioning
            LiveCanvas.UpdateLayout();
            AnimateCoverflowLive();
        }

        private void AnimateCoverflowLive()
        {
            if (_windows.Count == 0)
                return;

            // Layout params roughly aligned with the 3D coverflow settings
            const double offset = 400;
            const double centerScale = 1.25;
            const double scaleFalloff = 0.88;
            const double maxSkew = 18; // degrees (visual proxy for Y-rotation)

            double canvasW = LiveCanvas.ActualWidth;
            double canvasH = LiveCanvas.ActualHeight;
            if (canvasW <= 1 || canvasH <= 1)
            {
                canvasW = ActualWidth;
                canvasH = ActualHeight;
            }

            double centerX = canvasW / 2.0;
            double centerY = canvasH / 2.0;

            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            TimeSpan duration = TimeSpan.FromMilliseconds(220);

            foreach (var kvp in _liveCardByIndex)
            {
                int index = kvp.Key;
                var element = kvp.Value;
                if (!_transform2dPartsByElement.TryGetValue(element, out var parts))
                    continue;

                int distance = index - _currentIndex;
                int abs = Math.Abs(distance);
                double dir = distance == 0 ? 0 : Math.Sign(distance);

                double x = centerX + (distance * offset);
                double y = centerY;

                double s = distance == 0 ? centerScale : Math.Max(0.68, Math.Pow(scaleFalloff, abs));
                double skew = distance == 0 ? 0 : (-dir * maxSkew);

                Panel.SetZIndex(element, 1000 - abs);

                double tx = x - (element.Width / 2.0);
                double ty = y - (element.Height / 2.0);

                AnimateDouble(parts.Translate, TranslateTransform.XProperty, tx, duration, ease);
                AnimateDouble(parts.Translate, TranslateTransform.YProperty, ty, duration, ease);
                AnimateDouble(parts.Scale, ScaleTransform.ScaleXProperty, s, duration, ease);
                AnimateDouble(parts.Scale, ScaleTransform.ScaleYProperty, s, duration, ease);
                AnimateDouble(parts.Skew, SkewTransform.AngleYProperty, skew, duration, ease);
            }
        }

        private TransformParts EnsureTransformParts(ModelVisual3D visual)
        {
            if (_transformPartsByVisual.TryGetValue(visual, out var existing))
                return existing;

            var scale = new ScaleTransform3D(1, 1, 1);
            var axis = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            var rotate = new RotateTransform3D(axis);
            var translate = new TranslateTransform3D(0, 0, 0);

            var group = new Transform3DGroup();
            group.Children.Add(scale);
            group.Children.Add(rotate);
            group.Children.Add(translate);

            visual.Transform = group;

            var parts = new TransformParts
            {
                Scale = scale,
                Rotate = rotate,
                AxisAngle = axis,
                Translate = translate
            };

            _transformPartsByVisual[visual] = parts;
            return parts;
        }

        private static void AnimateDouble(Animatable target, DependencyProperty property, double to, TimeSpan duration, IEasingFunction easing)
        {
            var anim = new DoubleAnimation(to, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            };
            target.BeginAnimation(property, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private ModelVisual3D CreateWindowVisual(WindowInfo window, int index)
        {
            var visual = new ModelVisual3D();
            var modelGroup = new Model3DGroup();

            // --- Main Card ---
            var cardMesh = new MeshGeometry3D();
            double width = CardMeshWidth;
            double height = CardMeshHeight;
            
            cardMesh.Positions.Add(new Point3D(-width/2, -height/2, 0));
            cardMesh.Positions.Add(new Point3D(width/2, -height/2, 0));
            cardMesh.Positions.Add(new Point3D(width/2, height/2, 0));
            cardMesh.Positions.Add(new Point3D(-width/2, height/2, 0));

            cardMesh.TextureCoordinates.Add(new Point(0, 1));
            cardMesh.TextureCoordinates.Add(new Point(1, 1));
            cardMesh.TextureCoordinates.Add(new Point(1, 0));
            cardMesh.TextureCoordinates.Add(new Point(0, 0));

            cardMesh.TriangleIndices.Add(0);
            cardMesh.TriangleIndices.Add(1);
            cardMesh.TriangleIndices.Add(2);
            cardMesh.TriangleIndices.Add(0);
            cardMesh.TriangleIndices.Add(2);
            cardMesh.TriangleIndices.Add(3);

            var cardBitmap = RenderCardBitmap(window, index);
            var brush = new ImageBrush(cardBitmap) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);

            var emissiveBrush = brush.Clone();
            emissiveBrush.Opacity = 0.35;
            emissiveBrush.Freeze();

            var cardMaterial = new MaterialGroup();
            cardMaterial.Children.Add(new DiffuseMaterial(brush));
            cardMaterial.Children.Add(new EmissiveMaterial(emissiveBrush));
            
            var cardModel = new GeometryModel3D
            {
                Geometry = cardMesh,
                Material = cardMaterial,
                BackMaterial = cardMaterial
            };
            modelGroup.Children.Add(cardModel);

            // --- Reflection ---
            var reflectionMesh = new MeshGeometry3D();
            double reflectionHeight = height * 0.7; // Reflection is not full height
            double gap = 2.0; // Small gap between card and reflection

            reflectionMesh.Positions.Add(new Point3D(-width / 2, -height / 2 - reflectionHeight - gap, 0));
            reflectionMesh.Positions.Add(new Point3D(width / 2, -height / 2 - reflectionHeight - gap, 0));
            reflectionMesh.Positions.Add(new Point3D(width / 2, -height / 2 - gap, 0));
            reflectionMesh.Positions.Add(new Point3D(-width / 2, -height / 2 - gap, 0));

            reflectionMesh.TextureCoordinates.Add(new Point(0, 1));
            reflectionMesh.TextureCoordinates.Add(new Point(1, 1));
            reflectionMesh.TextureCoordinates.Add(new Point(1, 0));
            reflectionMesh.TextureCoordinates.Add(new Point(0, 0));

            reflectionMesh.TriangleIndices.Add(0);
            reflectionMesh.TriangleIndices.Add(1);
            reflectionMesh.TriangleIndices.Add(2);
            reflectionMesh.TriangleIndices.Add(0);
            reflectionMesh.TriangleIndices.Add(2);
            reflectionMesh.TriangleIndices.Add(3);

            var reflectionBrush = new VisualBrush();
            var reflectionVisual = new System.Windows.Shapes.Rectangle
            {
                Width = cardBitmap.PixelWidth,
                Height = cardBitmap.PixelHeight,
                Fill = new ImageBrush(cardBitmap) { Stretch = Stretch.Fill },
                OpacityMask = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(100, 255, 255, 255), 0.0), // Start semi-opaque white
                        new GradientStop(Colors.Transparent, 0.7)               // Fade to transparent
                    },
                    new Point(0.5, 0), new Point(0.5, 1)) // Vertical gradient
            };
            reflectionBrush.Visual = reflectionVisual;

            var reflectionMaterial = new DiffuseMaterial(reflectionBrush);

            var reflectionModel = new GeometryModel3D
            {
                Geometry = reflectionMesh,
                Material = reflectionMaterial,
                BackMaterial = reflectionMaterial
            };
            modelGroup.Children.Add(reflectionModel);

            visual.Content = modelGroup;

            _modelsByIndex[index] = cardModel; // Still track the main card model for texture updates
            
            SetCoverflowIndex(visual, index);

            return visual;
        }

        private BitmapSource RenderCardBitmap(WindowInfo window, int index)
        {
            var dpi = VisualTreeHelper.GetDpi(this);

            // Fixed resolution for crisp rendering
            const int cardW = 1600;
            const int cardH = 1000;
            const int footerH = 100;
            const int previewH = cardH - footerH;

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                // Clear to fully transparent so any letterboxing is transparent (not grey/black).
                dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, cardW, cardH));

                const double cornerRadius = 20;

                // Clip the full card to rounded corners (does not crop the preview content; only rounds edges).
                var cardClip = new RectangleGeometry(new Rect(0, 0, cardW, cardH), cornerRadius, cornerRadius);
                dc.PushClip(cardClip);

                // Preview area - show full preview (no cropping) with transparent letterboxing.
                Rect previewRect = new Rect(0, 0, cardW, previewH);
                if (window.Thumbnail != null)
                {
                    var thumbBrush = new ImageBrush(window.Thumbnail)
                    {
                        Stretch = Stretch.Uniform,
                        TileMode = TileMode.None,
                        AlignmentX = AlignmentX.Center,
                        AlignmentY = AlignmentY.Center
                    };

                    // Many windows include a thin non-client edge/shadow in captures.
                    // Trim a small margin to eliminate the visible grey/black bars without changing layout.
                    // Use pixel-based trim converted to relative units, with conservative clamps.
                    // This avoids over-cropping small windows (which can make them look like "title bar only").
                    int tw = Math.Max(1, window.Thumbnail.PixelWidth);
                    int th = Math.Max(1, window.Thumbnail.PixelHeight);

                    int leftPx = Math.Min(8, Math.Max(2, tw / 320));
                    int topPx = Math.Min(8, Math.Max(2, th / 320));

                    // Do not trim right/bottom to avoid cutting content; artifacts are reported on top/left.
                    int rightPx = 0;
                    int bottomPx = 0;

                    // For very small captures, keep only a tiny trim so the border doesn't come back,
                    // but never large enough to turn the preview into "title-bar only".
                    if (tw < 600 || th < 400)
                    {
                        leftPx = 2;
                        topPx = 2;
                    }

                    double cropLeft = (double)leftPx / tw;
                    double cropTop = (double)topPx / th;
                    double cropRight = (double)rightPx / tw;
                    double cropBottom = (double)bottomPx / th;

                    if ((cropLeft + cropRight) < 0.10 && (cropTop + cropBottom) < 0.10)
                    {
                        thumbBrush.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox;
                        thumbBrush.Viewbox = new Rect(
                            cropLeft,
                            cropTop,
                            1.0 - (cropLeft + cropRight),
                            1.0 - (cropTop + cropBottom));
                    }

                    RenderOptions.SetBitmapScalingMode(thumbBrush, BitmapScalingMode.Fant);
                    dc.DrawRectangle(thumbBrush, null, previewRect);
                }

                // Icon with proper sizing (no stretching)
                const double iconSize = 56;
                const double iconX = 20;
                double iconY = previewH + (footerH - iconSize) / 2;
                if (window.Icon != null)
                {
                    dc.DrawImage(window.Icon, new Rect(iconX, iconY, iconSize, iconSize));
                }

                // Title with proper font sizing
                string title = string.IsNullOrWhiteSpace(window.Title) ? "(untitled)" : window.Title;
                if (title.Length > 45) title = title.Substring(0, 45) + "...";

                var ft = new FormattedText(
                    title,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    26,
                    new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    1.0);

                double textX = iconX + iconSize + 16;
                double textY = previewH + (footerH - ft.Height) / 2;
                dc.DrawText(ft, new Point(textX, textY));
                
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(cardW, cardH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void PreloadThumbnailsAroundCurrent()
        {
            if (_windows.Count == 0)
                return;

            for (int offset = -2; offset <= 2; offset++)
            {
                int idx = _currentIndex + offset;
                if (idx < 0 || idx >= _windows.Count)
                    continue;

                if (_windows[idx].Thumbnail != null)
                    continue;

                try
                {
                    // Allow screen capture here because the switcher is still hidden
                    var thumb = _windowManager.CaptureThumbnail(_windows[idx].Handle, allowScreenCapture: true);
                    if (thumb != null)
                    {
                        _windows[idx].Thumbnail = thumb;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Thumbnail preload failed for window {idx}: {ex.Message}");
                }
            }
        }

        private void QueueThumbnailLoadAroundCurrent()
        {
            if (!_isVisible || _windows.Count == 0)
                return;

            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            _thumbCts = new System.Threading.CancellationTokenSource();
            var token = _thumbCts.Token;

            var indices = new HashSet<int>();
            for (int offset = -2; offset <= 2; offset++)
            {
                int idx = _currentIndex + offset;
                if (idx >= 0 && idx < _windows.Count)
                    indices.Add(idx);
            }

            foreach (int idx in indices)
            {
                if (_windows[idx].Thumbnail != null)
                    continue;

                IntPtr hWnd = _windows[idx].Handle;
                _ = System.Threading.Tasks.Task.Run(() =>
                {
                    if (token.IsCancellationRequested)
                        return;

                    // While the switcher is visible, avoid CopyFromScreen to prevent capturing our own overlay
                    var thumb = _windowManager.CaptureThumbnail(hWnd, allowScreenCapture: false);
                    if (thumb == null)
                        return;

                    Dispatcher.Invoke(() =>
                    {
                        if (token.IsCancellationRequested)
                            return;

                        if (idx < 0 || idx >= _windows.Count)
                            return;

                        _windows[idx].Thumbnail = thumb;
                        if (_modelsByIndex.TryGetValue(idx, out var model))
                        {
                            // Update material with a new composed card texture (preview + icon + title)
                            var card = RenderCardBitmap(_windows[idx], idx);
                            var brush = new ImageBrush(card)
                            {
                                Stretch = Stretch.Fill,
                                ViewportUnits = BrushMappingMode.Absolute,
                                TileMode = TileMode.None
                            };

                            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                            RenderOptions.SetCachingHint(brush, CachingHint.Cache);

                            var emissiveBrush = brush.Clone();
                            emissiveBrush.Opacity = 0.35;
                            emissiveBrush.Freeze();

                            var mat = new MaterialGroup();
                            mat.Children.Add(new DiffuseMaterial(brush));
                            mat.Children.Add(new EmissiveMaterial(emissiveBrush));
                            model.Material = mat;
                            model.BackMaterial = mat;
                        }
                    });
                }, token);
            }
        }

        private void AnimateCoverflow()
        {
            if (_useLiveDwmThumbnails)
            {
                AnimateCoverflowLive();
                return;
            }

            if (_windows.Count == 0) return;

            var visualsWithDistance = new List<(ModelVisual3D visual, int relativeIndex)>();

            // Easing tuned to feel closer to CoverflowAltTab's fast ease-out.
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            TimeSpan duration = TimeSpan.FromMilliseconds(220);

            for (int i = 0; i < Viewport.Children.Count; i++)
            {
                if (Viewport.Children[i] is ModelVisual3D visual)
                {
                    // Skip lights container
                    if (LightsVisual != null && ReferenceEquals(visual, LightsVisual))
                        continue;

                    int windowIndex = GetCoverflowIndex(visual);
                    if (windowIndex < 0 || windowIndex >= _windows.Count)
                        continue;

                    int relativeIndex = windowIndex - _currentIndex;

                    visualsWithDistance.Add((visual, relativeIndex));
                    
                    // Classic coverflow: smooth 3D carousel with proper spacing and visibility
                    const double coverflowWindowAngle = 50;
                    const double coverflowWindowOffsetWidth = 400;
                    const double previewScalingFactor = 0.88;
                    const double zOffset = -150;

                    double distance = Math.Abs(relativeIndex);
                    double xPos = relativeIndex * coverflowWindowOffsetWidth;
                    double yAngle = relativeIndex == 0 ? 0 : Math.Sign(relativeIndex) * coverflowWindowAngle;

                    // Center window prominent but not oversized, side windows clearly visible
                    double scale = relativeIndex == 0 ? 1.25 : Math.Pow(previewScalingFactor, distance);
                    if (scale < 0.68) scale = 0.68;

                    var parts = EnsureTransformParts(visual);

                    // Pivot trick: rotate around the edge closer to the center.
                    // Right side pivots around left edge; left side pivots around right edge.
                    if (relativeIndex > 0)
                        parts.Rotate.CenterX = -CardMeshWidth / 2.0;
                    else if (relativeIndex < 0)
                        parts.Rotate.CenterX = CardMeshWidth / 2.0;
                    else
                        parts.Rotate.CenterX = 0;

                    AnimateDouble(parts.Scale, ScaleTransform3D.ScaleXProperty, scale, duration, ease);
                    AnimateDouble(parts.Scale, ScaleTransform3D.ScaleYProperty, scale, duration, ease);
                    AnimateDouble(parts.Scale, ScaleTransform3D.ScaleZProperty, scale, duration, ease);

                    AnimateDouble(parts.AxisAngle, AxisAngleRotation3D.AngleProperty, yAngle, duration, ease);

                    AnimateDouble(parts.Translate, TranslateTransform3D.OffsetXProperty, xPos, duration, ease);
                    AnimateDouble(parts.Translate, TranslateTransform3D.OffsetYProperty, 0, duration, ease);
                    // Z-depth increases with distance for proper depth layering
                    AnimateDouble(parts.Translate, TranslateTransform3D.OffsetZProperty, zOffset - (distance * 20), duration, ease);
                }
            }

            // Layering: ensure farther previews render first, selected preview last (closest to GNOME behavior).
            if (visualsWithDistance.Count > 1)
            {
                var ordered = visualsWithDistance
                    .OrderByDescending(v => Math.Abs(v.relativeIndex))
                    .ThenBy(v => v.relativeIndex)
                    .Select(v => v.visual)
                    .ToList();

                ResetViewportToLightsOnly();
                foreach (var v in ordered)
                    Viewport.Children.Add(v);
            }
        }

        private void UpdateCurrentWindow()
        {
            if (_currentIndex < 0 || _currentIndex >= _windows.Count)
                return;

            var window = _windows[_currentIndex];
            
            CurrentTitle.Text = window.Title;
            CurrentSubtitle.Text = window.ProcessName;
            
            if (window.Icon != null)
            {
                CurrentIcon.Source = window.Icon;
            }
        }

        private void NavigateNext()
        {
            if (_windows.Count == 0) return;
            
            _currentIndex = (_currentIndex + 1) % _windows.Count;
            AnimateCoverflow();
            UpdateCurrentWindow();

            QueueThumbnailLoadAroundCurrent();
        }

        private void NavigatePrevious()
        {
            if (_windows.Count == 0) return;
            
            _currentIndex--;
            if (_currentIndex < 0)
                _currentIndex = _windows.Count - 1;
            
            AnimateCoverflow();
            UpdateCurrentWindow();

            QueueThumbnailLoadAroundCurrent();
        }

        private async void SelectWindow()
        {
            System.Diagnostics.Debug.WriteLine($"SelectWindow called, current index: {_currentIndex}, window count: {_windows.Count}");
            
            if (_currentIndex >= 0 && _currentIndex < _windows.Count)
            {
                var window = _windows[_currentIndex];
                System.Diagnostics.Debug.WriteLine($"Switching to window: {window.Title} (Handle: {window.Handle})");
                
                try
                {
                    // Hide switcher first to show the desktop
                    _isVisible = false;
                    this.Opacity = 0.0;
                    this.Visibility = Visibility.Hidden;
                    this.WindowState = WindowState.Minimized;

                    // Let WPF render the hide before switching focus
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    
                    // Reset keyboard state immediately
                    _keyboardHook.ResetState();
                    
                    // Small delay to ensure UI is hidden
                    await System.Threading.Tasks.Task.Delay(50);
                    
                    // Now switch to the window
                    _windowManager.SwitchToWindow(window.Handle);
                    System.Diagnostics.Debug.WriteLine("Window switch successful");
                    
                    // Clear 3D models after switching (keep lights)
                    ResetViewportToLightsOnly();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error switching window: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Invalid window index");
                HideSwitcher();
            }
        }

        private void CloseCurrentWindow()
        {
            if (_currentIndex >= 0 && _currentIndex < _windows.Count)
            {
                var window = _windows[_currentIndex];
                _windowManager.CloseWindow(window.Handle);
                
                // Refresh window list
                _windows.RemoveAt(_currentIndex);
                if (_currentIndex >= _windows.Count)
                    _currentIndex = _windows.Count - 1;
                
                if (_windows.Count == 0)
                {
                    HideSwitcher();
                }
                else
                {
                    BuildCoverflow();
                    UpdateCurrentWindow();
                }
            }
        }

        private void ShowDesktop()
        {
            _windowManager.ShowDesktop();
            HideSwitcher();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Right:
                case Key.Tab when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    NavigateNext();
                    e.Handled = true;
                    break;

                case Key.Left:
                case Key.Tab when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                    NavigatePrevious();
                    e.Handled = true;
                    break;

                case Key.Enter:
                case Key.Space:
                    SelectWindow();
                    e.Handled = true;
                    break;

                case Key.Escape:
                    HideSwitcher();
                    e.Handled = true;
                    break;

                case Key.Q:
                    CloseCurrentWindow();
                    e.Handled = true;
                    break;

                case Key.D:
                    ShowDesktop();
                    e.Handled = true;
                    break;

                case Key.F1:
                    HelpOverlay.Visibility = HelpOverlay.Visibility == Visibility.Visible
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                    e.Handled = true;
                    break;
            }
        }

        private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0)
            {
                NavigatePrevious();
            }
            else
            {
                NavigateNext();
            }
            e.Handled = true;
        }

        protected override void OnClosed(EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow closing - cleaning up");
            
            // Unregister keyboard hook first
            _keyboardHook?.Unregister();
            _keyboardHook?.Dispose();
            
            // Dispose tray service
            _trayService?.Dispose();

            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            
            System.Diagnostics.Debug.WriteLine("Cleanup complete");
            
            base.OnClosed(e);
        }
    }
}
