using System.Runtime.InteropServices;

namespace HWSuite.Gui.Services;

/// <summary>
/// Holds off a Windows restart or shutdown while the maintenance runs and until the technician answers the restart
/// question: a hidden top-level window answers "no" to WM_QUERYENDSESSION and gives Windows the reason to show
/// ("HWSuite: maintenance in progress"), so an installer or Windows Update asking for a restart has to wait. A forced
/// restart (one that does not ask the running programs) cannot be held by any program.
/// </summary>
internal sealed partial class ShutdownBlocker : IDisposable
{
    private const string WindowClass = "HWSuite.ShutdownBlocker";
    private const uint WM_QUERYENDSESSION = 0x0011;
    private const uint WM_ENDSESSION = 0x0016;
    private static readonly WndProc Proc = WindowProc; // kept alive: the window class calls it through a native pointer
    private static bool _registered;
    private static ShutdownBlocker? _active;

    private readonly IntPtr _window;
    private bool _disposed;

    public ShutdownBlocker(string reason)
    {
        var instance = GetModuleHandleW(null);
        if (!_registered)
        {
            var cls = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                hInstance = instance,
                lpszClassName = WindowClass,
            };
            _registered = RegisterClassExW(ref cls) != 0;
        }

        _active = this;
        _window = CreateWindowExW(0, WindowClass, "HWSuite", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window != IntPtr.Zero)
        {
            ShutdownBlockReasonCreate(_window, reason);
        }
    }

    /// <summary>True when Windows asked to restart or shut down while blocked (and was told to wait).</summary>
    public bool RestartWasAsked { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ReferenceEquals(_active, this))
        {
            _active = null;
        }

        if (_window != IntPtr.Zero)
        {
            ShutdownBlockReasonDestroy(_window);
            DestroyWindow(_window);
        }
    }

    private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WM_QUERYENDSESSION)
        {
            if (_active is { } blocker)
            {
                blocker.RestartWasAsked = true;
            }

            return IntPtr.Zero; // not now
        }

        if (message == WM_ENDSESSION)
        {
            return IntPtr.Zero;
        }

        return DefWindowProcW(hwnd, message, wParam, lParam);
    }

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonCreate(IntPtr window, string reason);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShutdownBlockReasonDestroy(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);
}
