using System.Runtime.InteropServices;

namespace HWSuite.Gui.Services;

/// <summary>
/// Notification-area icon built on Shell_NotifyIcon. WinUI 3 has no tray API, and the shell's own
/// notifications (balloons) keep working when the app runs as administrator, unlike app toasts.
/// Owns a hidden window on the UI thread that receives the icon's mouse messages, so every event
/// is raised on the UI thread.
/// </summary>
public sealed partial class TrayIcon : IDisposable
{
    /// <summary>Window class of the hidden window; a second instance finds the first one by it.</summary>
    public const string WindowClass = "HWSuite.TrayWindow";

    /// <summary>Message a second instance posts to bring the running one to the front.</summary>
    public const uint ShowMessage = WM_APP + 2;

    private const uint WM_APP = 0x8000;
    private const uint CallbackMessage = WM_APP + 1;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_NULL = 0x0000;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;
    private const uint NIN_BALLOONUSERCLICK = 0x0405;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    private const uint NIIF_INFO = 0x1, NIIF_WARNING = 0x2, NIIF_LARGE_ICON = 0x20;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;
    private const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    private const uint MSGFLT_ALLOW = 1;
    private const int CommandOpen = 1, CommandExit = 2;

    private readonly WndProc _proc; // kept alive: the window calls it through a native pointer
    private readonly IntPtr _window;
    private readonly IntPtr _icon;
    private readonly uint _taskbarCreated;
    private readonly string _openText;
    private readonly string _exitText;
    private string _tip = "HWSuite";
    private bool _added;
    private bool _disposed;

    public TrayIcon(string iconPath, string openText, string exitText)
    {
        _openText = openText;
        _exitText = exitText;
        _proc = WindowProc;
        var instance = GetModuleHandleW(null);
        var cls = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = instance,
            lpszClassName = WindowClass,
        };
        RegisterClassExW(ref cls);

        // A real (never shown) top-level window, not a message-only one: it must receive the
        // TaskbarCreated broadcast to put the icon back after Explorer restarts.
        _window = CreateWindowExW(0, WindowClass, "HWSuite", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");

        // When elevated, let the (non-elevated) shell and a second app instance reach this window.
        foreach (var message in new[] { _taskbarCreated, CallbackMessage, ShowMessage, WM_COMMAND, WM_CONTEXTMENU })
        {
            ChangeWindowMessageFilterEx(_window, message, MSGFLT_ALLOW, IntPtr.Zero);
        }

        _icon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, GetSystemMetrics(49 /* SM_CXSMICON */), GetSystemMetrics(50 /* SM_CYSMICON */), LR_LOADFROMFILE);
        Add();
    }

    /// <summary>Left click, balloon click or "Open" in the menu.</summary>
    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    /// <summary>Tooltip text (the shell keeps at most 127 characters).</summary>
    public void SetTooltip(string text)
    {
        _tip = text.Length > 127 ? text[..127] : text;
        var data = NewData(NIF_TIP | NIF_SHOWTIP);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>Windows notification from the icon (shown as a toast on Windows 10/11).</summary>
    public void Notify(string title, string message, bool warning)
    {
        var data = NewData(NIF_INFO);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = message.Length > 255 ? message[..255] : message;
        data.dwInfoFlags = (warning ? NIIF_WARNING : NIIF_INFO) | NIIF_LARGE_ICON;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        DestroyWindow(_window);
        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
        }
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
    }

    private NOTIFYICONDATAW NewData(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == CallbackMessage)
        {
            switch ((uint)(lParam.ToInt64() & 0xFFFF))
            {
                case WM_LBUTTONUP:
                case WM_LBUTTONDBLCLK:
                case NIN_BALLOONUSERCLICK:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case WM_RBUTTONUP:
                case WM_CONTEXTMENU:
                    ShowMenu();
                    break;
            }

            return IntPtr.Zero;
        }

        if (message == ShowMessage)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
            return IntPtr.Zero;
        }

        if (message == _taskbarCreated && _taskbarCreated != 0)
        {
            Add(); // Explorer restarted and forgot every icon
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenuW(menu, MF_STRING, CommandOpen, _openText);
            AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, CommandExit, _exitText);
            GetCursorPos(out var point);
            SetForegroundWindow(_window); // otherwise the menu does not close when clicking elsewhere
            var command = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, point.X, point.Y, _window, IntPtr.Zero);
            PostMessageW(_window, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            if (command == CommandOpen)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (command == CommandExit)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Asks a running instance (found by its tray window) to show itself.</summary>
    public static bool SignalRunningInstance()
    {
        var window = FindWindowW(WindowClass, null);
        return window != IntPtr.Zero && PostMessageW(window, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    public bool IsAdded => _added;

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW cls);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr window, uint message, uint action, IntPtr filterStatus);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr instance, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, nint id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? className, string? windowName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? name);
}
