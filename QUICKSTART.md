# Quick Start Guide

## Prerequisites

Before running Windows Coverflow, make sure you have:

1. **Windows 10 or Windows 11**
2. **.NET 8.0 SDK** - [Download here](https://dotnet.microsoft.com/download/dotnet/8.0)

To check if .NET 8.0 is installed, run:
```powershell
dotnet --version
```

## Running the Application

### Option 1: Quick Run (Development)

1. Open PowerShell in the project directory
2. Run:
```powershell
.\build-and-run.ps1
```

This will build and start the application automatically.

### Option 2: Build and Run Manually

1. Build the project:
```powershell
dotnet build -c Release
```

2. Run the executable:
```powershell
.\WindowsCoverflow\bin\Release\net8.0-windows\WindowsCoverflow.exe
```

### Option 3: Create Standalone Executable

For distribution, create a publishable version:

```powershell
.\publish.ps1
```

This creates two versions:
- **Framework-dependent** (smaller, requires .NET 8.0 Runtime)
- **Self-contained** (larger, includes everything)

## First Use

1. **Launch the app** - It will appear in your system tray (notification area)
2. **Press Alt+Tab** - The coverflow switcher will appear
3. **Navigate**:
   - Use arrow keys or mouse wheel to browse windows
   - Press Enter to select a window
   - Press Esc to cancel
4. **System Tray** - Right-click the icon for options

## Troubleshooting

### "The command 'dotnet' is not recognized"

Install the .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0

### Alt+Tab not working

- Check if the app is running (look in system tray)
- Try running as Administrator:
```powershell
Start-Process -FilePath ".\WindowsCoverflow\bin\Release\net8.0-windows\WindowsCoverflow.exe" -Verb RunAs
```

### Build errors

Restore NuGet packages:
```powershell
dotnet restore
dotnet clean
dotnet build
```

## Keyboard Shortcuts (While Switcher is Open)

| Shortcut | Action |
|----------|--------|
| `→` or `↓` | Next window |
| `←` or `↑` | Previous window |
| `Tab` | Next window |
| `Shift+Tab` | Previous window |
| `Mouse Wheel` | Navigate windows |
| `Enter` or `Space` | Select window |
| `Esc` | Cancel |
| `Q` | Close selected window |
| `D` | Show desktop |
| `F1` | Toggle help |

## Development

To modify the code:

1. Open in Visual Studio 2022 or VS Code
2. Edit files in the `WindowsCoverflow` folder
3. Rebuild with `dotnet build`

### Project Structure

```
WindowsCoverflow/
├── Models/
│   └── WindowInfo.cs           # Window data model
├── Services/
│   ├── WindowManager.cs        # Window capture & management
│   ├── KeyboardHook.cs         # Global hotkey handler
│   └── SystemTrayService.cs    # System tray integration
├── MainWindow.xaml             # UI layout
├── MainWindow.xaml.cs          # UI logic
├── App.xaml                    # Application resources
└── App.xaml.cs                 # Application entry point
```

## Tips

- The app runs in the background - check system tray
- Window thumbnails update when you open the switcher
- You can middle-click or Q to close unwanted windows
- Use D to quickly minimize everything and show desktop

Enjoy your new coverflow window switcher! 🎉
