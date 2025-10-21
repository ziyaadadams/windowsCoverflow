# Windows Coverflow

A beautiful coverflow-style Alt-Tab window switcher for Windows, inspired by the GNOME Coverflow extension.

![Windows Coverflow](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![License](https://img.shields.io/badge/license-GPL--3.0-green)

## ✨ Features

- **3D Coverflow Effect**: Navigate through your windows with a stunning 3D coverflow animation
- **Global Hotkey**: Press `Alt+Tab` to open the switcher
- **Multiple Navigation Options**:
  - Arrow keys for precise control
  - Mouse wheel for quick scrolling
  - Tab key to cycle through windows
- **Window Management**:
  - Press `Enter` or `Space` to switch to selected window
  - Press `Q` to close the current window
  - Press `D` to show desktop (minimize all)
  - Press `Esc` to cancel
- **Background Service**: Runs silently in the system tray
- **Window Thumbnails**: Live window previews captured via Windows DWM API
- **Smart Filtering**: Automatically hides tool windows, invisible windows, and system windows

## 🚀 Installation

### Prerequisites

- Windows 10 or Windows 11
- .NET 8.0 Runtime (Desktop)

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
dotnet run --project WindowsCoverflow\WindowsCoverflow.csproj
```

### Creating an Executable

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

The executable will be in `WindowsCoverflow\bin\Release\net8.0-windows\win-x64\publish\`

## 📖 Usage

### Starting the Application

Simply run `WindowsCoverflow.exe`. The application will:
- Start minimized to the system tray
- Register a global `Alt+Tab` hotkey
- Wait for you to trigger the window switcher

### Keyboard Controls

| Key | Action |
|-----|--------|
| `Alt+Tab` | Open/cycle through windows |
| `→` / `←` | Navigate next/previous window |
| `Tab` | Navigate next window |
| `Shift+Tab` | Navigate previous window |
| `Enter` / `Space` | Switch to selected window |
| `Esc` | Cancel and close switcher |
| `Q` | Close the currently selected window |
| `D` | Show desktop (minimize all windows) |
| `F1` | Show/hide help overlay |

### Mouse Controls

- **Mouse Wheel Up**: Navigate to previous window
- **Mouse Wheel Down**: Navigate to next window

### System Tray

Right-click the system tray icon for options:
- **Show Switcher**: Manually open the window switcher
- **Settings**: Configure preferences (coming soon)
- **About**: View application information
- **Exit**: Close the application

## 🎨 How It Works

The application uses several Windows technologies:

1. **Win32 Low-Level Keyboard Hook**: Captures `Alt+Tab` globally before Windows processes it
2. **DWM (Desktop Window Manager) API**: Captures live window thumbnails
3. **WPF 3D**: Renders the coverflow effect using `Viewport3D` and perspective transformations
4. **Window Enumeration**: Uses `EnumWindows` to discover all visible windows

### Architecture

```
WindowsCoverflow
├── Models
│   └── WindowInfo.cs          # Window data model
├── Services
│   ├── WindowManager.cs       # Window enumeration & capture
│   ├── KeyboardHook.cs        # Global hotkey handler
│   └── SystemTrayService.cs   # System tray integration
├── MainWindow.xaml            # 3D Coverflow UI
└── App.xaml                   # Application entry point
```

## 🔧 Configuration

Currently, configuration is done through the Settings dialog (accessible from system tray).

### Planned Features

- [ ] Customizable keybindings
- [ ] Animation speed control
- [ ] Window filtering options
- [ ] Theme customization
- [ ] Multi-monitor support
- [ ] Window grouping by application
- [ ] Search/filter windows by name

## 🐛 Troubleshooting

### Alt+Tab not working

- Make sure the application is running (check system tray)
- Run the application as Administrator if needed
- Check if another application is capturing Alt+Tab

### Windows not showing thumbnails

- Ensure Windows Aero/DWM is enabled
- Some protected windows (e.g., UAC prompts) cannot be captured
- Try closing and reopening the switcher

### Application crashes on startup

- Verify .NET 8.0 Runtime is installed
- Check Windows Event Viewer for error details
- Run from command line to see error messages

## 📝 License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

Inspired by the [GNOME Coverflow Alt-Tab extension](https://github.com/dsheeler/CoverflowAltTab) by dsheeler and contributors.

## 🙏 Acknowledgments

- GNOME Coverflow Alt-Tab extension for the inspiration
- Microsoft WPF team for the excellent 3D rendering capabilities
- The open-source community

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

1. Fork the project
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📧 Contact

Project Link: [https://github.com/yourusername/windowsCoverflow](https://github.com/yourusername/windowsCoverflow)

---

**Note**: This is an independent implementation for Windows and is not affiliated with the original GNOME extension project.
