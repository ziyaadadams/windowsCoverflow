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
        private const int VK_SHIFT = 0x10;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;

        private IntPtr _hookId = IntPtr.Zero;
        private static LowLevelKeyboardProc? _proc;
        private bool _altPressed = false;
        private bool _isHandlingAltTab = false;
        private bool _shiftPressed = false;

        public sealed class AltTabEventArgs : EventArgs
        {
            public bool Reverse { get; }
            public AltTabEventArgs(bool reverse) => Reverse = reverse;
        }

        public event EventHandler<AltTabEventArgs>? AltTabPressed;
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
        
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        
        private const uint KEYEVENTF_KEYUP = 0x0002;

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

            // For WH_KEYBOARD_LL, passing hMod = IntPtr.Zero is valid and often more reliable for managed apps.
            _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);

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

        public void Unregister()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                
                // Reset all states
                _altPressed = false;
                _isHandlingAltTab = false;
                
                Debug.WriteLine("Keyboard hook unregistered and state reset");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int vkCode = kbStruct.vkCode;

                bool isKeyDown = wParam == (IntPtr)WM_SYSKEYDOWN || wParam == (IntPtr)WM_KEYDOWN;
                bool isKeyUp = wParam == (IntPtr)WM_SYSKEYUP || wParam == (IntPtr)WM_KEYUP;

                // Track Shift state (so Shift+Alt+Tab works even if we block key delivery)
                if (isKeyDown && (vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT))
                    _shiftPressed = true;
                else if (isKeyUp && (vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT))
                    _shiftPressed = false;

                // While we are handling Alt-Tab, block everything except Alt/Shift and Tab.
                if (_isHandlingAltTab &&
                    vkCode != VK_MENU && vkCode != VK_LMENU && vkCode != VK_RMENU &&
                    vkCode != VK_TAB &&
                    vkCode != VK_SHIFT && vkCode != VK_LSHIFT && vkCode != VK_RSHIFT)
                {
                    return (IntPtr)1;
                }

                // Check for Alt key down
                if (isKeyDown)
                {
                    if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                    {
                        _altPressed = true;
                    }
                    // Check for Tab key while Alt is pressed
                    else if (vkCode == VK_TAB)
                    {
                        // Double-check Alt state using GetAsyncKeyState
                        bool altIsDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                        
                        if (_altPressed || altIsDown)
                        {
                            _isHandlingAltTab = true;
                            
                            try
                            {
                                bool shiftIsDown = _shiftPressed || (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                                AltTabPressed?.Invoke(this, new AltTabEventArgs(shiftIsDown));
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
                else if (isKeyUp)
                {
                    // Block Tab key up too (helps prevent native switcher in some configurations)
                    if (vkCode == VK_TAB)
                    {
                        bool altIsDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                        if (_altPressed || altIsDown || _isHandlingAltTab)
                            return (IntPtr)1;
                    }

                    // Block shift key up/down while handling to prevent leaking input to underlying apps
                    if (_isHandlingAltTab && (vkCode == VK_SHIFT || vkCode == VK_LSHIFT || vkCode == VK_RSHIFT))
                    {
                        return (IntPtr)1;
                    }

                    if (vkCode == VK_MENU || vkCode == VK_LMENU || vkCode == VK_RMENU)
                    {
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

        public void ResetState()
        {
            _altPressed = false;
            _isHandlingAltTab = false;
            _shiftPressed = false;
            
            // Force release all Alt keys
            keybd_event((byte)VK_LMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_RMENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            
            Debug.WriteLine("Keyboard hook state forcefully reset and Alt keys released");
        }

        public void Dispose()
        {
            Unregister();
        }
    }
}
