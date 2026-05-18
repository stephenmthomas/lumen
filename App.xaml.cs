using System.Windows;
using System.Windows.Interop;
using System.IO;
using DisplayControl.Services;

namespace DisplayControl;

public partial class App : Application
{
    private NativeTrayIcon? _trayIcon;
    private TrayContextMenu? _contextMenu;
    private SettingsWindow? _settingsWindow;
    private DisplayService? _displayService;
    private HotkeyService? _hotkeyService;
    private FilterService? _filterService;
    private SettingsService? _settingsService;
    private ICCProfileService? _iccProfileService;




    protected override void OnStartup(StartupEventArgs e)
    {

        // Set up exception handlers first
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
        {
            // Enable message boxes before showing error
            if (MainWindow is SettingsWindow sw)
            {
                sw._allowNativeChromeMessages = true;
            }

            var crashLog = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                "ulumen_crash.txt"
            );
            File.WriteAllText(crashLog, ex.ExceptionObject.ToString());
            MessageBox.Show($"μLumen crashed. See ulumen_crash.txt on Desktop.",
                            "Crash", MessageBoxButton.OK, MessageBoxImage.Error);
        };



        base.OnStartup(e);

        
       
        // Initialize services
        _displayService = new DisplayService();
        _filterService = new FilterService();
        _settingsService = new SettingsService();
        _iccProfileService = new ICCProfileService();


        

        _settingsService.Load();

        // Create settings window
        _settingsWindow = new SettingsWindow(_displayService, _filterService, _settingsService, _iccProfileService);
        _settingsWindow.Show();

        // Now get window handle for hotkey registration
        var helper = new WindowInteropHelper(_settingsWindow);
        var hwndSource = HwndSource.FromHwnd(helper.Handle);

        //Set Display Service , DXGI vs GDI
        _displayService.ActiveBackend = DisplayBackend.GDI;

        // Initialize hotkey service
        _hotkeyService = new HotkeyService(helper.Handle);
        RegisterHotkeys();

        // Hook up WndProc for hotkey messages AND tray icon messages
        hwndSource.AddHook(WndProc);

        // Create native tray icon
        CreateTrayIcon(helper.Handle);
        
        // position and show main form appropriately
        if (_settingsService.Settings.StartMinimized) _settingsWindow.Hide();
        _settingsWindow.Left = (System.Windows.SystemParameters.PrimaryScreenWidth / 2) - (_settingsWindow.Width / 2);
        _settingsWindow.Top = (System.Windows.SystemParameters.PrimaryScreenHeight / 2) - (_settingsWindow.Height / 2);


    }

    private void CreateTrayIcon(IntPtr hwnd)
    {
        // Create native tray icon
        _trayIcon = new NativeTrayIcon(hwnd);
        _trayIcon.Create("app.ico", "Display Control");

        // Double-click shows settings window
        _trayIcon.DoubleClick += (s, e) =>
        {
            _settingsWindow?.Show();
            _settingsWindow?.Activate();
        };

        // Right-click shows context menu
        _trayIcon.RightClick += (s, position) => ShowContextMenu(position);
    }

    private void ShowContextMenu(Point position)
    {
        if (_contextMenu == null)
        {
            _contextMenu = new TrayContextMenu();

            // Build menu structure
            _contextMenu.AddItem("Show Settings", () =>
            {
                _settingsWindow?.Show();
                _settingsWindow?.Activate();
            });

            _contextMenu.AddSeparator();

            // Presets submenu
            var presetsMenu = _contextMenu.AddSubmenu("Presets");
            presetsMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "Default",
                Command = new RelayCommand(() => _displayService?.ApplyColorProfile(ColorProfile.Default))
            });
            presetsMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "Night Mode",
                Command = new RelayCommand(() => _displayService?.ApplyColorProfile(ColorProfile.Night))
            });
            presetsMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "Reading",
                Command = new RelayCommand(() => _displayService?.ApplyColorProfile(ColorProfile.Reading))
            });
            presetsMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "Gaming",
                Command = new RelayCommand(() => _displayService?.ApplyColorProfile(ColorProfile.Gaming))
            });

            _contextMenu.AddItem("Reset to System Default", () => _displayService?.ResetAll());

            _contextMenu.AddSeparator();

            _contextMenu.AddItem("Exit", () =>
            {
                _displayService?.ResetAll();
                Shutdown();
            });
        }

        _contextMenu.Show(position);
    }

    private void RegisterHotkeys()
    {
        if (_hotkeyService == null || _settingsWindow == null || _displayService == null)
            return;

        // Brightness: Ctrl+Alt+Up/Down
        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Alt,
            Hotkey.Keys.Up,
            () => _settingsWindow.AdjustBrightness(5));

        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Alt,
            Hotkey.Keys.Down,
            () => _settingsWindow.AdjustBrightness(-5));

        // Contrast: Ctrl+Alt+Left/Right
        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Alt,
            Hotkey.Keys.Left,
            () => _settingsWindow.AdjustContrast(-5));

        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Alt,
            Hotkey.Keys.Right,
            () => _settingsWindow.AdjustContrast(5));

        // Gamma: Ctrl+Shift+Up/Down
        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Shift,
            Hotkey.Keys.Up,
            () => _settingsWindow.AdjustGamma(5));

        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Shift,
            Hotkey.Keys.Down,
            () => _settingsWindow.AdjustGamma(-5));

        // Color Temperature: Ctrl+Shift+Left/Right
        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Shift,
            Hotkey.Keys.Left,
            () => _settingsWindow.AdjustColorTemperature(-100));

        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Shift,
            Hotkey.Keys.Right,
            () => _settingsWindow.AdjustColorTemperature(100));

        // Reset: Ctrl+Alt+R
        _hotkeyService.RegisterHotkey(
            Hotkey.Control | Hotkey.Alt,
            0x52, // 'R' key
            () => _settingsWindow.ResetToDefaults());
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Let tray icon handle its messages
        _trayIcon?.ProcessWindowMessage(hwnd, msg, wParam, lParam, ref handled);

        // Let hotkey service handle its messages
        if (!handled && _hotkeyService != null)
        {
            handled = _hotkeyService.ProcessHotkey(msg, wParam);
        }

        return IntPtr.Zero;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeyService?.Dispose();
        _displayService?.Dispose();
        _trayIcon?.Dispose();
        _filterService?.Dispose();

        base.OnExit(e);
    }
}

/// <summary>
/// Simple relay command implementation for menu bindings.
/// </summary>
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { System.Windows.Input.CommandManager.RequerySuggested += value; }
        remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute();
    }

    public void Execute(object? parameter)
    {
        _execute();
    }
}