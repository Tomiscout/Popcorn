using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Popcorn.Player.Windows.Utils;

internal static class Win32Functions
{
    #region Window creation
    [DllImport("user32.dll", EntryPoint = "CreateWindowEx", CharSet = CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(int dwExStyle,
                                                 string lpClassName,
                                                 string lpWindowName,
                                                 int dwStyle,
                                                 int x,
                                                 int y,
                                                 int nWidth,
                                                 int nHeight,
                                                 IntPtr hWndParent,
                                                 IntPtr hMenu,
                                                 IntPtr hInstance,
                                                 [MarshalAs(UnmanagedType.AsAny)]
                                                 object lpParam);

    [DllImport("user32.dll", EntryPoint = "DestroyWindow", CharSet = CharSet.Unicode)]
    internal static extern bool DestroyWindow(IntPtr hwnd);
    #endregion

    #region Window state
    public const int GWL_STYLE = -16;
    public const int WS_POPUP = unchecked((int)0x80000000);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    #endregion

    #region Screen working area
    public static RECT GetScreenWorkingArea()
    {
        RECT workArea = new RECT();
        SystemParametersInfo(SPI_GETWORKAREA, 0, ref workArea, 0);
        return workArea;
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(int uiAction, int uiParam, ref RECT pvParam, int fWinIni);

    private const int SPI_GETWORKAREA = 0x0030;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    #endregion
}
