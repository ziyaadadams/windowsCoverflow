using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsCoverflow.Services
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_TAB = 0x09;
        private const int VK_LMENU = 0xA4;  // Left Alt
        private const int VK_RMENU = 0xA5;  // Right Alt
        private const int VK_MENU = 0x12;   // Alt key (generic)

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _proc;
        private bool _altPressed = false;
        private bool _isHandlingAltTab = false;

        public event EventHandler? AltTabPressed;
        public event EventHandler? AltReleased;

        #region Win32 API

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public int vkCode;
            public int scanCode;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        #endregion

        public void Register()
        {
            _proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            
            if (curModule != null)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
                
                if (_hookId == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"Failed to register keyboard hook. Error: {errorCode}");
                }
                else
                {
                    Debug.WriteLine("Keyboard hook registered successfully");
                }
            }
        }

        public void Unregister()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                Debug.WriteLine("Keyboard hook unregistered");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = kbStruct.vkCode;

                // Check for Alt key down
                if (wParam == (IntPtr)WM_SYSKEYDOWN || wParam == (IntPtr)WM_KEYDOWN)
                {
                    if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                    {
                        _altPressed = true;
                        Debug.WriteLine("Alt pressed");
                    }
                    // Check for Tab key while Alt is pressed
                    else if (vkCode == VK_TAB)
                    {
                        // Double-check Alt state using GetAsyncKeyState
                        bool altIsDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                        
                        if (_altPressed || altIsDown)
                        {
                            Debug.WriteLine("Alt+Tab detected - invoking event");
                            _isHandlingAltTab = true;
                            
                            try
                            {
                                AltTabPressed?.Invoke(this, EventArgs.Empty);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error in AltTabPressed event: {ex.Message}");
                            }
                            
                            // Block the default Alt+Tab behavior
                            return (IntPtr)1;
                        }
                    }
                }
                // Check for Alt key up
                else if (wParam == (IntPtr)WM_SYSKEYUP || wParam == (IntPtr)WM_KEYUP)
                {
                    if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                    {
                        Debug.WriteLine("Alt released");
                        _altPressed = false;
                        
                        if (_isHandlingAltTab)
                        {
                            _isHandlingAltTab = false;
                            AltReleased?.Invoke(this, EventArgs.Empty);
                            return (IntPtr)1; // Block this too while we're handling
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        public void Dispose()
        {
            Unregister();
        }
    }
}
