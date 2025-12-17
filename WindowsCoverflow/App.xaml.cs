using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;

namespace WindowsCoverflow
{
    public partial class App : Application
    {
        private static Mutex? _mutex;

        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Ensure only one instance is running
            _mutex = new Mutex(true, "WindowsCoverflowSingleInstance", out bool createdNew);
            
            if (!createdNew)
            {
                MessageBox.Show(
                    "Windows Coverflow is already running.\nCheck your system tray.",
                    "Already Running",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
            
            // Create a single hidden window that owns hooks/tray
            _mainWindow = new MainWindow();
            MainWindow = _mainWindow;
            // Do not show; it activates on Alt+Tab
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}
