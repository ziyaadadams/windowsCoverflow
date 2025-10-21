# Windows Coverflow - Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Windows Coverflow                         │
│                  Background Service (WPF)                    │
└─────────────────────────────────────────────────────────────┘
                              │
                    ┌─────────┴─────────┐
                    │                   │
         ┌──────────▼─────────┐  ┌─────▼──────────┐
         │   System Tray      │  │  Keyboard Hook │
         │   (NotifyIcon)     │  │  (Alt+Tab)     │
         └──────────┬─────────┘  └─────┬──────────┘
                    │                   │
                    │         ┌─────────▼──────────┐
                    │         │  MainWindow        │
                    └────────►│  (Hidden/Visible)  │
                              └─────────┬──────────┘
                                        │
                    ┌───────────────────┼───────────────────┐
                    │                   │                   │
          ┌─────────▼────────┐  ┌──────▼──────┐  ┌────────▼─────────┐
          │  WindowManager   │  │  3D Viewport │  │  Event Handlers  │
          │  (Win32 API)     │  │  (Coverflow) │  │  (Keyboard/Mouse)│
          └──────────────────┘  └──────────────┘  └──────────────────┘
```

## Component Details

### 1. Application Layer (App.xaml.cs)
- Entry point for the application
- Initializes all services
- Manages application lifecycle

### 2. System Tray Service
- Runs in the background
- Provides system tray icon
- Context menu for settings/exit
- Events: ShowSwitcher, Exit

### 3. Keyboard Hook Service
- Low-level Windows keyboard hook
- Intercepts Alt+Tab before Windows
- Events: AltTabPressed
- Uses: `SetWindowsHookEx` Win32 API

### 4. Window Manager Service
**Purpose**: Enumerate and manage Windows

**Win32 APIs Used**:
- `EnumWindows` - List all windows
- `GetWindowText` - Get window titles
- `PrintWindow` - Capture thumbnails
- `DwmGetWindowAttribute` - Check window state
- `SetForegroundWindow` - Switch windows
- `SendMessage(WM_CLOSE)` - Close windows

**Features**:
- Filters invisible/tool windows
- Captures window thumbnails
- Gets application icons
- Detects minimized state

### 5. Main Window (Coverflow UI)
**Technology**: WPF with 3D rendering

**Components**:
- `Viewport3D` - 3D scene container
- `PerspectiveCamera` - View perspective
- `ModelVisual3D` - Individual window cards
- `GeometryModel3D` - 3D mesh geometry
- `ImageBrush` - Window thumbnail texture

**Animations**:
- Position transforms (X, Z)
- Rotation (Y-axis)
- Scale (center window larger)

## Data Flow

### Opening the Switcher

```
User presses Alt+Tab
      │
      ▼
KeyboardHook detects combination
      │
      ▼
AltTabPressed event fired
      │
      ▼
MainWindow.ShowSwitcher()
      │
      ├─► WindowManager.GetWindows()
      │   └─► Enumerate & capture all windows
      │
      ├─► Build 3D coverflow scene
      │   └─► Create ModelVisual3D for each window
      │
      └─► Show window (full screen, transparent)
```

### Navigating Windows

```
User presses Arrow Key / Mouse Wheel
      │
      ▼
MainWindow.NavigateNext/Previous()
      │
      ├─► Update current index
      │
      ├─► AnimateCoverflow()
      │   └─► Apply 3D transforms to all models
      │
      └─► UpdateCurrentWindow()
          └─► Update title/icon display
```

### Selecting a Window

```
User presses Enter
      │
      ▼
MainWindow.SelectWindow()
      │
      ├─► WindowManager.SwitchToWindow(handle)
      │   ├─► Restore if minimized
      │   └─► SetForegroundWindow()
      │
      └─► Hide switcher
```

## 3D Coverflow Math

### Position Calculation
```csharp
int relativeIndex = i - currentIndex;
double xPos = relativeIndex * 280;      // Horizontal spacing
double zPos = Math.Abs(relativeIndex) * -100;  // Depth
double yAngle = relativeIndex * 45;     // Rotation
double scale = (i == currentIndex) ? 1.2 : 0.8;
```

### Transform Pipeline
1. **Scale** - Make center window larger
2. **Rotate** - Angle windows on Y-axis
3. **Translate** - Position in 3D space

### Visual Effect
```
        [Window -2]              [Window 2]
              ╲                      ╱
               ╲                    ╱
                ╲                  ╱
             [Window -1]      [Window 1]
                    ╲          ╱
                     ╲        ╱
                   [Window 0]
                 (Current - Large)
```

## Performance Considerations

### Window Capture
- Thumbnail size limited to 400x300
- Captured on demand when switcher opens
- Uses hardware-accelerated `PrintWindow`

### 3D Rendering
- WPF hardware acceleration enabled
- Smooth 300ms animations
- Viewport3D clips for performance

### Memory
- Window list refreshed on each open
- Old thumbnails discarded
- No background monitoring

## Extension Points

### Future Enhancements
1. **Settings System**: Add configuration file (JSON)
2. **Custom Themes**: Color schemes, animations
3. **Window Grouping**: Group by application
4. **Search/Filter**: Type to filter windows
5. **Multi-Monitor**: Per-monitor window lists
6. **Animations**: Configurable speed/style

### Adding New Features

**To add a new keyboard shortcut**:
1. Add case to `MainWindow.Window_KeyDown()`
2. Implement handler method
3. Update help overlay

**To modify 3D effect**:
1. Edit position calculations in `AnimateCoverflow()`
2. Adjust camera position/FOV in XAML
3. Modify mesh geometry in `CreateWindowVisual()`

**To add window filters**:
1. Edit `WindowManager.GetWindows()`
2. Add filter conditions in `EnumWindows` callback
3. Test with various window types

## Security Considerations

- Requires Administrator rights for some protected windows
- Cannot capture UAC prompts or secure desktop
- Low-level keyboard hook needs user consent
- No network access or data collection

## Dependencies

- **.NET 8.0** - Runtime framework
- **System.Drawing.Common** - Icon/image processing
- **System.Windows.Forms** - System tray (NotifyIcon)
- **Windows 10/11** - Win32 APIs

## Build Pipeline

```
Source Code
    │
    ▼
dotnet restore  ← Fetch NuGet packages
    │
    ▼
dotnet build    ← Compile C#/XAML
    │
    ▼
dotnet publish  ← Create executable
    │
    ├─► Framework-dependent build
    └─► Self-contained build
```

---

**Note**: This architecture closely mirrors the GNOME Coverflow extension but adapted for Windows using WPF and Win32 APIs.
