using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DisplayControl;

/// <summary>
/// Native Windows system tray icon using P/Invoke.
/// Zero dependencies, pure Win32 API.
/// </summary>
public class NativeTrayIcon : IDisposable
{
    private const int WM_TRAYICON = 0x8000;
    private const uint NIF_MESSAGE = 0x01;
    private const uint NIF_ICON = 0x02;
    private const uint NIF_TIP = 0x04;
    private const uint NIM_ADD = 0x00;
    private const uint NIM_MODIFY = 0x01;
    private const uint NIM_DELETE = 0x02;
    private const uint WM_LBUTTONDBLCLK = 0x203;
    private const uint WM_RBUTTONUP = 0x205;

    private readonly IntPtr _hwnd;
    private readonly uint _uid;
    private IntPtr _hIcon;
    private bool _isAdded;

    public event EventHandler? DoubleClick;
    public event EventHandler<Point>? RightClick;

    public NativeTrayIcon(IntPtr ownerHwnd)
    {
        _hwnd = ownerHwnd;
        _uid = 1;
    }

    public void Create(string iconPath, string tooltip)
    {
        // Load icon from file
        _hIcon = LoadImage(IntPtr.Zero, iconPath, 1, 16, 16, 0x00000010);
        
        if (_hIcon == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to load icon: {iconPath}");

        // Create tray icon
        var nid = new NOTIFYICONDATA();
        nid.cbSize = Marshal.SizeOf(nid);
        nid.hWnd = _hwnd;
        nid.uID = _uid;
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        nid.uCallbackMessage = WM_TRAYICON;
        nid.hIcon = _hIcon;
        nid.szTip = tooltip;

        if (!Shell_NotifyIcon(NIM_ADD, ref nid))
            throw new InvalidOperationException("Failed to add tray icon");

        _isAdded = true;
    }

    public void ProcessWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            uint message = (uint)lParam & 0xFFFF;
            
            if (message == WM_LBUTTONDBLCLK)
            {
                DoubleClick?.Invoke(this, EventArgs.Empty);
                handled = true;
            }
            else if (message == WM_RBUTTONUP)
            {
                GetCursorPos(out POINT pt);
                RightClick?.Invoke(this, new Point(pt.X, pt.Y));
                handled = true;
            }
        }
    }

    public void Dispose()
    {
        if (_isAdded)
        {
            var nid = new NOTIFYICONDATA();
            nid.cbSize = Marshal.SizeOf(nid);
            nid.hWnd = _hwnd;
            nid.uID = _uid;
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            _isAdded = false;
        }

        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }
    }

    // P/Invoke declarations
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA pnid);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hInst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
