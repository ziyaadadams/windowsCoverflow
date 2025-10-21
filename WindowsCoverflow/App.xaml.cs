using System.Windows;

namespace WindowsCoverflow
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Initialize the global keyboard hook
            var mainWindow = new MainWindow();
            mainWindow.Hide(); // Start hidden, will show on Alt+Tab
        }
    }
}
