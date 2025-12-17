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
using System.Windows.Threading;
using WindowsCoverflow.Services;
using WindowsCoverflow.Models;

namespace WindowsCoverflow
{
    public partial class MainWindow : Window
    {
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
        private System.Threading.CancellationTokenSource? _thumbCts;

        private sealed class TransformParts
        {
            public required ScaleTransform3D Scale { get; init; }
            public required RotateTransform3D Rotate { get; init; }
            public required AxisAngleRotation3D AxisAngle { get; init; }
            public required TranslateTransform3D Translate { get; init; }
        }

        public MainWindow()
        {
            InitializeComponent();

            // Enable subtle blur behind the switcher background (best-effort; silently fails if unsupported)
            this.SourceInitialized += (_, __) =>
            {
                try { EnableBlurBehind(); } catch { }
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

        [DllImport("dwmapi.dll")]
        private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        private void EnableBlurBehind()
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

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

        private void HideSwitcher()
        {
            _isVisible = false;

            _thumbCts?.Cancel();
            _thumbCts?.Dispose();
            _thumbCts = null;
            
            // Reset keyboard hook state to release Alt key
            _keyboardHook.ResetState();

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

            // Higher base resolution for sharper previews
            const int baseCardW = 1600;
            const int baseCardH = 1000;
            const int baseFooterH = 92;

            double dpiScale = Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);
            int cardW = (int)Math.Round(baseCardW * dpiScale);
            int cardH = (int)Math.Round(baseCardH * dpiScale);
            int footerH = (int)Math.Round(baseFooterH * dpiScale);
            int previewH = cardH - footerH;

            // Clamp to avoid excessive memory/CPU on very high DPI.
            cardW = Math.Min(cardW, 3200);
            cardH = Math.Min(cardH, 2000);
            footerH = Math.Min(footerH, 220);
            previewH = cardH - footerH;

            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen())
            {
                // No background - fully transparent to show blur effect

                // Preview area
                Rect previewRect = new Rect(0, 0, cardW, previewH);
                if (window.Thumbnail != null)
                {
                    var thumbBrush = new ImageBrush(window.Thumbnail)
                    {
                        Stretch = Stretch.Uniform,
                        TileMode = TileMode.None
                    };
                    RenderOptions.SetBitmapScalingMode(thumbBrush, BitmapScalingMode.Fant);
                    dc.DrawRectangle(thumbBrush, null, previewRect);
                }
                else
                {
                    // No fallback background - keep transparent
                }

                // Icon with proper sizing (no stretching)
                double iconSize = Math.Round(48 * dpiScale);
                double iconX = Math.Round(16 * dpiScale);
                double iconY = previewH + (footerH - iconSize) / 2;
                if (window.Icon != null)
                {
                    dc.PushClip(new RectangleGeometry(new Rect(iconX, iconY, iconSize, iconSize)));
                    dc.DrawImage(window.Icon, new Rect(iconX, iconY, iconSize, iconSize));
                    dc.Pop();
                }

                // Title with proper font sizing
                string title = string.IsNullOrWhiteSpace(window.Title) ? "(untitled)" : window.Title;
                if (title.Length > 45) title = title.Substring(0, 45) + "...";

                var ft = new FormattedText(
                    title,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    Math.Round(22 * dpiScale),
                    new SolidColorBrush(Color.FromRgb(230, 230, 230)),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip);

                double textX = iconX + iconSize + Math.Round(12 * dpiScale);
                double textY = previewH + (footerH - ft.Height) / 2;
                dc.DrawText(ft, new Point(textX, textY));
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
                    
                    // Focused window at 50% of screen, side windows clearly visible
                    const double coverflowWindowAngle = 55;
                    const double coverflowWindowOffsetWidth = 380;
                    const double previewScalingFactor = 0.8;
                    const double zOffset = -200;

                    double distance = Math.Abs(relativeIndex);
                    double xPos = relativeIndex * coverflowWindowOffsetWidth;
                    double yAngle = relativeIndex == 0 ? 0 : Math.Sign(relativeIndex) * coverflowWindowAngle;

                    double scale = relativeIndex == 0 ? 1.3 : Math.Pow(previewScalingFactor, distance);
                    if (scale < 0.70) scale = 0.70;

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
                    // Tiny epsilon per index avoids z-fighting when things get close.
                    AnimateDouble(parts.Translate, TranslateTransform3D.OffsetZProperty, zOffset - (distance * 0.01), duration, ease);
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
