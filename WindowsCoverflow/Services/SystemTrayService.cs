using System;
using System.Windows.Forms;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using Application = System.Windows.Application;

namespace WindowsCoverflow.Services
{
    public class SystemTrayService : IDisposable
    {
        private NotifyIcon? _notifyIcon;

        public event EventHandler? ExitRequested;
        public event EventHandler? ShowSwitcherRequested;

        public SystemTrayService()
        {
            InitializeTrayIcon();
        }

        public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon?.ShowBalloonTip(2000, title, message, icon);
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = CreateTaskSwitcherIcon(),
                Visible = true,
                Text = "Windows Coverflow - Alt+Tab Replacement"
            };

            // Create context menu
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("Show Switcher");
            showItem.Click += (s, e) => ShowSwitcherRequested?.Invoke(this, EventArgs.Empty);
            
            var settingsItem = new ToolStripMenuItem("Settings");
            settingsItem.Click += (s, e) => ShowSettings();
            
            var aboutItem = new ToolStripMenuItem("About");
            aboutItem.Click += (s, e) => ShowAbout();
            
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);

            contextMenu.Items.Add(showItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(aboutItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowSwitcherRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ShowSettings()
        {
            WpfMessageBox.Show(
                "Settings feature coming soon!\n\nCurrently supports:\n" +
                "• Alt+Tab to open switcher\n" +
                "• Arrow keys / Mouse wheel to navigate\n" +
                "• Enter to select\n" +
                "• Esc to cancel\n" +
                "• Q to close window\n" +
                "• D to show desktop\n" +
                "• F1 for help",
                "Windows Coverflow Settings",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }

        private void ShowAbout()
        {
            WpfMessageBox.Show(
                "Windows Coverflow Alt-Tab v1.0\n\n" +
                "A beautiful coverflow-style window switcher for Windows,\n" +
                "inspired by the GNOME Coverflow extension.\n\n" +
                "© 2025 - Licensed under GPL-3.0",
                "About Windows Coverflow",
                WpfMessageBoxButton.OK,
                WpfMessageBoxImage.Information);
        }

        private System.Drawing.Icon CreateTaskSwitcherIcon()
        {
            // Create a 32x32 bitmap for the icon
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using var g = System.Drawing.Graphics.FromImage(bmp);
            
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);

            // Draw three overlapping rectangles representing windows in coverflow
            var blue = System.Drawing.Color.FromArgb(255, 74, 144, 226);
            var lightBlue = System.Drawing.Color.FromArgb(255, 91, 163, 245);
            var darkBlue = System.Drawing.Color.FromArgb(255, 53, 122, 189);

            // Left window (back, darker)
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, lightBlue.R, lightBlue.G, lightBlue.B)))
            using (var pen = new System.Drawing.Pen(darkBlue, 1.5f))
            {
                g.FillRectangle(brush, 3, 10, 10, 14);
                g.DrawRectangle(pen, 3, 10, 10, 14);
            }

            // Right window (back, darker)
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, lightBlue.R, lightBlue.G, lightBlue.B)))
            using (var pen = new System.Drawing.Pen(darkBlue, 1.5f))
            {
                g.FillRectangle(brush, 19, 10, 10, 14);
                g.DrawRectangle(pen, 19, 10, 10, 14);
            }

            // Center window (focused, brighter and larger)
            using (var brush = new System.Drawing.SolidBrush(blue))
            using (var pen = new System.Drawing.Pen(darkBlue, 2f))
            {
                g.FillRectangle(brush, 10, 6, 12, 18);
                g.DrawRectangle(pen, 10, 6, 12, 18);
                
                // Add window title bar indication
                using (var titleBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(100, 255, 255, 255)))
                {
                    g.FillRectangle(titleBrush, 10, 6, 12, 3);
                }
            }

            return System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
