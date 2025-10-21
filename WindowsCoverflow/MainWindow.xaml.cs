using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using WindowsCoverflow.Services;
using WindowsCoverflow.Models;

namespace WindowsCoverflow
{
    public partial class MainWindow : Window
    {
        private readonly WindowManager _windowManager;
        private readonly KeyboardHook _keyboardHook;
        private readonly SystemTrayService _trayService;
        
        private List<WindowInfo> _windows = new();
        private int _currentIndex = 0;
        private bool _isVisible = false;

        public MainWindow()
        {
            InitializeComponent();
            
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

            Loaded += (s, e) => this.Hide();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Hide on startup
            this.Visibility = Visibility.Hidden;
        }

        private void OnAltTabPressed(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnAltTabPressed called");
            
            // Use Dispatcher to ensure we're on the UI thread
            Dispatcher.Invoke(() =>
            {
                if (!_isVisible)
                {
                    ShowSwitcher();
                }
                else
                {
                    NavigateNext();
                }
            });
        }

        private void OnAltReleased(object? sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnAltReleased called");
            
            // When Alt is released, select the current window
            Dispatcher.Invoke(() =>
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
            _windows = _windowManager.GetWindows();
            
            System.Diagnostics.Debug.WriteLine($"Found {_windows.Count} windows");
            
            if (_windows.Count == 0)
                return;

            _currentIndex = 0;
            _isVisible = true;

            // Show and focus
            this.Visibility = Visibility.Visible;
            this.Activate();
            this.Focus();

            // Build 3D coverflow
            BuildCoverflow();
            UpdateCurrentWindow();
        }

        private void HideSwitcher()
        {
            _isVisible = false;
            this.Visibility = Visibility.Hidden;
            
            // Clear 3D models
            Viewport.Children.Clear();
        }

        private void BuildCoverflow()
        {
            Viewport.Children.Clear();

            for (int i = 0; i < _windows.Count; i++)
            {
                var window = _windows[i];
                var visual = CreateWindowVisual(window, i);
                Viewport.Children.Add(visual);
            }

            AnimateCoverflow();
        }

        private ModelVisual3D CreateWindowVisual(WindowInfo window, int index)
        {
            var visual = new ModelVisual3D();
            
            // Create a plane for the window thumbnail
            var mesh = new MeshGeometry3D();
            
            // Define rectangle vertices
            double width = 300;
            double height = 200;
            
            mesh.Positions.Add(new Point3D(-width/2, -height/2, 0));
            mesh.Positions.Add(new Point3D(width/2, -height/2, 0));
            mesh.Positions.Add(new Point3D(width/2, height/2, 0));
            mesh.Positions.Add(new Point3D(-width/2, height/2, 0));

            // Texture coordinates
            mesh.TextureCoordinates.Add(new Point(0, 1));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));

            // Triangle indices
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(1);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0);
            mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(3);

            // Create material with window thumbnail
            var material = new DiffuseMaterial();
            
            if (window.Thumbnail != null)
            {
                var brush = new ImageBrush(window.Thumbnail);
                material.Brush = brush;
            }
            else
            {
                // Fallback to solid color
                material.Brush = new SolidColorBrush(Color.FromRgb(60, 60, 60));
            }

            var model = new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material
            };

            visual.Content = model;
            
            // Store index as a tag
            visual.SetValue(FrameworkElement.TagProperty, index);

            return visual;
        }

        private void AnimateCoverflow()
        {
            if (_windows.Count == 0) return;

            for (int i = 0; i < Viewport.Children.Count; i++)
            {
                if (Viewport.Children[i] is ModelVisual3D visual)
                {
                    int relativeIndex = i - _currentIndex;
                    
                    // Calculate position and rotation
                    double xPos = relativeIndex * 280; // Horizontal spacing
                    double zPos = Math.Abs(relativeIndex) * -100; // Depth
                    double yAngle = relativeIndex * 45; // Rotation angle

                    // Scale - center window is larger
                    double scale = i == _currentIndex ? 1.2 : 0.8;

                    var transform = new Transform3DGroup();
                    
                    // Scale
                    transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
                    
                    // Rotate
                    transform.Children.Add(new RotateTransform3D(
                        new AxisAngleRotation3D(new Vector3D(0, 1, 0), yAngle)));
                    
                    // Translate
                    transform.Children.Add(new TranslateTransform3D(xPos, 0, zPos));

                    // Animate the transform
                    AnimateTransform(visual, transform, TimeSpan.FromMilliseconds(300));
                }
            }
        }

        private void AnimateTransform(ModelVisual3D visual, Transform3D targetTransform, TimeSpan duration)
        {
            visual.Transform = targetTransform;
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
        }

        private void NavigatePrevious()
        {
            if (_windows.Count == 0) return;
            
            _currentIndex--;
            if (_currentIndex < 0)
                _currentIndex = _windows.Count - 1;
            
            AnimateCoverflow();
            UpdateCurrentWindow();
        }

        private void SelectWindow()
        {
            if (_currentIndex >= 0 && _currentIndex < _windows.Count)
            {
                var window = _windows[_currentIndex];
                _windowManager.SwitchToWindow(window.Handle);
            }
            HideSwitcher();
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
            _keyboardHook.Unregister();
            _trayService.Dispose();
            base.OnClosed(e);
        }
    }
}
