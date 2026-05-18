using System.Windows.Interop;
using DisplayControl.Native;

namespace DisplayControl.Services;

/// <summary>
/// Manages global hotkeys that work even when the application doesn't have focus.
/// </summary>
public class HotkeyService : IDisposable
{
    private readonly IntPtr _windowHandle;
    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private int _currentId = 1;
    private bool _disposed;

    public HotkeyService(IntPtr windowHandle)
    {
        _windowHandle = windowHandle;
    }

    /// <summary>
    /// Registers a global hotkey and associates it with an action.
    /// Returns the hotkey ID for later unregistration.
    /// </summary>
    public int RegisterHotkey(uint modifiers, uint key, Action action)
    {
        int id = _currentId++;
        
        // MOD_NOREPEAT prevents the hotkey from repeating when held down
        uint mods = modifiers | NativeMethods.MOD_NOREPEAT;
        
        if (NativeMethods.RegisterHotKey(_windowHandle, id, mods, key))
        {
            _hotkeyActions[id] = action;
            return id;
        }

        return -1;
    }

    /// <summary>
    /// Unregisters a hotkey by its ID.
    /// </summary>
    public void UnregisterHotkey(int id)
    {
        if (_hotkeyActions.ContainsKey(id))
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
            _hotkeyActions.Remove(id);
        }
    }

    /// <summary>
    /// Handles Windows messages for hotkey events.
    /// Call this from your WndProc override.
    /// </summary>
    public bool ProcessHotkey(int msg, IntPtr wParam)
    {
        const int WM_HOTKEY = 0x0312;
        
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action.Invoke();
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // Unregister all hotkeys
        foreach (var id in _hotkeyActions.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
        }

        _hotkeyActions.Clear();
        _disposed = true;
        
        GC.SuppressFinalize(this);
    }

    ~HotkeyService()
    {
        Dispose();
    }
}

/// <summary>
/// Helper class for creating hotkey combinations.
/// </summary>
public static class Hotkey
{
    public static uint None => 0;
    public static uint Alt => NativeMethods.MOD_ALT;
    public static uint Control => NativeMethods.MOD_CONTROL;
    public static uint Shift => NativeMethods.MOD_SHIFT;
    public static uint Win => NativeMethods.MOD_WIN;

    public static class Keys
    {
        public static uint F1 => NativeMethods.VK_F1;
        public static uint F2 => NativeMethods.VK_F2;
        public static uint F3 => NativeMethods.VK_F3;
        public static uint F4 => NativeMethods.VK_F4;
        public static uint F5 => NativeMethods.VK_F5;
        public static uint F6 => NativeMethods.VK_F6;
        public static uint F7 => NativeMethods.VK_F7;
        public static uint F8 => NativeMethods.VK_F8;
        public static uint F9 => NativeMethods.VK_F9;
        public static uint F10 => NativeMethods.VK_F10;
        public static uint F11 => NativeMethods.VK_F11;
        public static uint F12 => NativeMethods.VK_F12;
        
        public static uint Up => NativeMethods.VK_UP;
        public static uint Down => NativeMethods.VK_DOWN;
        public static uint Left => NativeMethods.VK_LEFT;
        public static uint Right => NativeMethods.VK_RIGHT;
        
        public static uint Plus => NativeMethods.VK_OEM_PLUS;
        public static uint Minus => NativeMethods.VK_OEM_MINUS;
    }
}
