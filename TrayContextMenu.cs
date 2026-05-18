using System;
using System.Windows;
using System.Windows.Controls;

namespace DisplayControl;

/// <summary>
/// Helper to show WPF ContextMenu at cursor position for tray icon.
/// This lets us use XAML-style menus without WinForms.
/// </summary>
public class TrayContextMenu
{
    private readonly ContextMenu _menu;
    private Window? _dummyWindow;

    public TrayContextMenu()
    {
        _menu = new ContextMenu();
        _menu.Closed += (s, e) => _dummyWindow?.Close();
    }

    public ContextMenu Menu => _menu;

    public void Show(Point cursorPosition)
    {
        // WPF ContextMenu needs a PlacementTarget (a visual element)
        // Create invisible window as the target
        if (_dummyWindow == null || !_dummyWindow.IsLoaded)
        {
            _dummyWindow = new Window
            {
                Width = 1,
                Height = 1,
                Left = cursorPosition.X,
                Top = cursorPosition.Y,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.Transparent,
                AllowsTransparency = true,
                Topmost = true
            };
        }

        _dummyWindow.Left = cursorPosition.X;
        _dummyWindow.Top = cursorPosition.Y;
        _dummyWindow.Show();

        _menu.PlacementTarget = _dummyWindow;
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        _menu.HorizontalOffset = 0;
        _menu.VerticalOffset = 0;
        _menu.IsOpen = true;

        // Bring menu to front and set foreground window
        _dummyWindow.Activate();
        SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(_dummyWindow).Handle);
    }

    // P/Invoke to bring menu to foreground properly
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public MenuItem AddItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (s, e) => onClick();
        _menu.Items.Add(item);
        return item;
    }

    public void AddSeparator()
    {
        _menu.Items.Add(new Separator());
    }

    public MenuItem AddSubmenu(string header)
    {
        var item = new MenuItem { Header = header };
        _menu.Items.Add(item);
        return item;
    }
}