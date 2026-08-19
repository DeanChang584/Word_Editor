using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WordFormatterUI.Services;

/// <summary>
/// Manages a system tray icon for the application using P/Invoke (Shell_NotifyIconW).
/// No external NuGet dependencies required.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    // ── Win32 Constants ──────────────────────────────────────────────
    private const uint WM_USER = 0x0400;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_SHOWTIP = 0x00000080;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    // ── Win32 Structures ─────────────────────────────────────────────
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
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    // ── Win32 Imports ────────────────────────────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint cmd, ref NOTIFYICONDATAW data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassW([In] ref WNDCLASSW lpWndClass);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string lpszName, uint uType, int cx, int cy, uint fuLoad);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint LR_DEFAULTSIZE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
    }

    // ── Fields ───────────────────────────────────────────────────────
    private const string WindowClass = "WordFormatterTrayWindow";
    private const uint TrayCallbackMsg = WM_USER + 1001;
    private const uint TrayIconId = 0;

    private IntPtr _hWnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _disposed;

    // 必须保存委托的强引用！GetFunctionPointerForDelegate 只返回函数指针，
    // 若委托被 GC 回收，隐藏窗口收到任何消息（托盘回调 / WM_DESTROY 等）都会
    // 触发 FailFast 崩溃（"callback was made on a garbage collected delegate"），
    // 表现为随机时机进程直接退出。字段持有引用后委托与类同生命周期。
    private WndProcDelegate? _wndProcDelegate;

    private readonly string _tooltip;
    private readonly Window _appWindow;

    /// <summary>
    /// Raised when the user wants to show/restore the main window.
    /// </summary>
    public event Action? ShowWindowRequested;

    /// <summary>
    /// Raised when the user wants to exit the application.
    /// </summary>
    public event Action? ExitRequested;

    public TrayIconService(Window appWindow, string tooltip = "WordFormatter")
    {
        _appWindow = appWindow;
        _tooltip = tooltip;
    }

    /// <summary>
    /// Creates the tray icon. Must be called from the UI thread (or after the window is created).
    /// </summary>
    public void Initialize()
    {
        if (_hWnd != IntPtr.Zero)
            return;

        var hInstance = GetModuleHandleW(null);

        // 保存委托强引用（见字段注释）——防 GC 回收后窗口消息触发 FailFast
        _wndProcDelegate = new WndProcDelegate(WndProc);

        // Register a message-only window class
        var wndClass = new WNDCLASSW
        {
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClass,
        };
        var atom = RegisterClassW(ref wndClass);
        if (atom == 0)
            throw new InvalidOperationException("RegisterClassW failed");

        // Create a message-only window (HWND_MESSAGE = -3)
        _hWnd = CreateWindowExW(
            0, WindowClass, "WordFormatterTrayWindow",
            0, 0, 0, 0, 0,
            new IntPtr(-3), IntPtr.Zero, hInstance, IntPtr.Zero);
        if (_hWnd == IntPtr.Zero)
            throw new InvalidOperationException("CreateWindowExW failed");

        // Load the icon from the .ico file embedded in the app
        var iconPath = System.IO.Path.Combine(
            AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        if (_hIcon == IntPtr.Zero)
        {
            // Fallback: try alternative path for development
            iconPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(typeof(App).Assembly.Location) ?? "",
                "Assets", "AppIcon.ico");
            _hIcon = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE | LR_DEFAULTSIZE);
        }

        // Add the tray icon
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP,
            uCallbackMessage = TrayCallbackMsg,
            hIcon = _hIcon,
            szTip = _tooltip,
        };
        if (!Shell_NotifyIconW(NIM_ADD, ref data))
            throw new InvalidOperationException("Shell_NotifyIconW (NIM_ADD) failed");
    }

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == TrayCallbackMsg)
        {
            var mouseMsg = (uint)lParam;
            if (mouseMsg == WM_LBUTTONUP)
            {
                ShowWindowRequested?.Invoke();
            }
            else if (mouseMsg == WM_RBUTTONUP)
            {
                ShowContextMenu();
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        // Create a simple context menu using the app window's dispatcher
        // Since WinUI 3 doesn't have built-in context menu for tray, we show a ContentDialog
        // or use the window's DispatcherQueue to trigger actions.
        // For simplicity, we'll just trigger ShowWindowRequested on double-click
        // and ExitRequested via a separate mechanism.
        // In a real app, you'd use a native HMENU with TrackPopupMenu.
        // For now, we rely on left-click to show, and the app's exit button.
        ShowWindowRequested?.Invoke();
    }

    /// <summary>
    /// Updates the tray icon tooltip text.
    /// </summary>
    public void SetTooltip(string text)
    {
        if (_hWnd == IntPtr.Zero) return;
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _hWnd,
            uID = TrayIconId,
            uFlags = NIF_TIP,
            szTip = text,
        };
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Remove tray icon
        if (_hWnd != IntPtr.Zero)
        {
            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
                hWnd = _hWnd,
                uID = TrayIconId,
            };
            Shell_NotifyIconW(NIM_DELETE, ref data);
        }

        // Clean up icon handle
        if (_hIcon != IntPtr.Zero)
        {
            DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        // Destroy the message-only window
        if (_hWnd != IntPtr.Zero)
        {
            DestroyWindow(_hWnd);
            _hWnd = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}