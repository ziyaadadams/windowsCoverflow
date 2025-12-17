# Windows Coverflow

A professional coverflow-style Alt-Tab window switcher for Windows, inspired by the GNOME Coverflow extension.

![Windows Coverflow](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-9.0-purple)
![License](https://img.shields.io/badge/license-GPL--3.0-green)

## Features

- **3D Coverflow Effect**: Navigate through your windows with a stunning 3D coverflow animation featuring smooth easing and realistic perspective
- **Global Hotkey Support**: Press `Alt+Tab` to open the switcher with system-wide capture
- **Multiple Navigation Options**:
  - Tab key to cycle through windows
  - Shift+Tab for reverse navigation
  - Mouse wheel for quick scrolling
- **Advanced Window Management**:
  - Press `Enter` to switch to selected window
  - Press `Esc` to cancel
  - Intelligent window state handling (minimized windows are properly restored)
- **Background Service**: Runs silently in the system tray with minimal resource usage
- **High-Quality Window Previews**: 
  - Windows Graphics Capture (WGC) integration for sharp, high-resolution previews
  - Fallback to PrintWindow API for maximum compatibility
  - Up to 2560x1600 capture resolution for crisp display
- **Visual Polish**:
  - Transparent background with desktop blur effect
  - 3D reflections for each window card
  - Smooth animations with quintic easing
  - Application icons and titles rendered directly on cards
- **Smart Filtering**: Automatically hides tool windows, invisible windows, and system windows

## Installation

### Prerequisites

- Windows 10 (version 19041 or later) or Windows 11
- .NET 9.0 Runtime (Desktop)

### Building from Source

1. Clone the repository:
```powershell
git clone https://github.com/yourusername/windowsCoverflow.git
cd windowsCoverflow
```

2. Build the project:
```powershell
dotnet build -c Release
```

3. Run the application:
```powershell
.\WindowsCoverflow\bin\Release\net9.0-windows10.0.19041.0\WindowsCoverflow.exe
```

### Creating a Standalone Executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The self-contained executable will be in `WindowsCoverflow\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish\`

## Usage

### Starting the Application

Run `WindowsCoverflow.exe`. The application will:
- Start minimized to the system tray
- Register a global `Alt+Tab` hotkey override
- Remain idle until triggered

### Keyboard Controls

| Key | Action |
|-----|--------|
| `Alt+Tab` | Open and cycle forward through windows |
| `Alt+Shift+Tab` | Cycle backward through windows |
| `Enter` | Switch to selected window and close switcher |
| `Esc` | Cancel and close switcher |

### Mouse Controls

- **Mouse Wheel Up**: Navigate to previous window
- **Mouse Wheel Down**: Navigate to next window

### System Tray

Right-click the system tray icon for options:
- **Show Switcher**: Manually open the window switcher
- **Exit**: Close the application

## Technical Details

### Core Technologies

1. **Low-Level Keyboard Hook**: Win32 `WH_KEYBOARD_LL` hook captures `Alt+Tab` before Windows processes it
2. **Windows Graphics Capture (WGC)**: Modern DirectX-based capture API for high-quality window previews
3. **Desktop Window Manager (DWM)**: Blur-behind effect for transparent background
4. **WPF 3D Rendering**: Hardware-accelerated `Viewport3D` with perspective camera and transform animations
5. **Window Enumeration**: `EnumWindows` API with intelligent filtering logic

### Architecture

```
WindowsCoverflow/
├── Models/
│   └── WindowInfo.cs              # Window metadata and thumbnail storage
├── Services/
│   ├── WindowManager.cs           # Window enumeration, filtering, and switching
│   ├── WindowsGraphicsCaptureService.cs  # WGC-based high-quality capture
│   ├── KeyboardHook.cs            # Global keyboard hook with Alt+Tab interception
│   └── SystemTrayService.cs       # System tray integration
├── MainWindow.xaml                # 3D Coverflow viewport and UI
├── MainWindow.xaml.cs             # Animation logic and rendering pipeline
└── App.xaml                       # Application configuration and styles
```

### Performance Characteristics

- **Memory Usage**: Approximately 50-100MB (varies with number of open windows)
- **CPU Usage**: Near-zero when idle; brief spike during window switching
- **Capture Performance**: Asynchronous thumbnail loading with preloading of adjacent windows
- **Animation Performance**: Hardware-accelerated 3D transforms at 60 FPS

## Configuration

### Current Parameters

The following visual parameters are configured in the source code:

- **Coverflow Angle**: 55 degrees
- **Window Offset**: 380 pixels
- **Side Window Scale**: 0.8x
- **Focused Window Scale**: 1.3x (approximately 50% of screen)
- **Animation Duration**: 220ms with quintic ease-out
- **Background Opacity**: 50%

### Future Enhancements

- Customizable keybindings
- Animation speed control
- Visual theme customization
- Multi-monitor awareness
- Window grouping by application
- Search and filter capabilities

## Troubleshooting

### Alt+Tab Not Working

- Verify the application is running (check system tray for icon)
- Run the application as Administrator if other applications are interfering
- Ensure no other Alt+Tab replacement tools are active

### Windows Not Showing Previews

- Confirm Desktop Window Manager (DWM) is enabled
- Some protected windows (UAC prompts, secure desktop) cannot be captured
- Try closing and reopening the switcher to refresh captures

### Application Crashes on Startup

- Verify .NET 9.0 Runtime is installed correctly
- Check Windows Event Viewer for detailed error information
- Run from command line to view console output

### Poor Performance or Lag

- Close unused windows to reduce memory usage
- Disable hardware acceleration in graphics settings if experiencing issues
- Check for Windows updates and graphics driver updates

## License

This project is licensed under the GNU General Public License v3.0. See the [LICENSE](LICENSE) file for complete details.

Inspired by the [GNOME Coverflow Alt-Tab extension](https://github.com/dsheeler/CoverflowAltTab) by dsheeler and contributors.

## Acknowledgments

- GNOME Coverflow Alt-Tab extension for the visual design inspiration
- Microsoft WPF and Windows Graphics Capture teams for excellent APIs
- The open-source community for tools and feedback

## Contributing

Contributions are welcome. Please follow these steps:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/YourFeature`)
3. Commit your changes with clear messages (`git commit -m 'Add YourFeature'`)
4. Push to your branch (`git push origin feature/YourFeature`)
5. Open a Pull Request with a detailed description

## Contact

Project Link: [https://github.com/yourusername/windowsCoverflow](https://github.com/yourusername/windowsCoverflow)

---

**Note**: This is an independent Windows implementation and is not affiliated with the original GNOME extension project.
