using System;
using System.Windows.Media.Imaging;

namespace WindowsCoverflow.Models
{
    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public BitmapSource? Thumbnail { get; set; }
        public BitmapSource? Icon { get; set; }
        public bool IsMinimized { get; set; }
    }
}
