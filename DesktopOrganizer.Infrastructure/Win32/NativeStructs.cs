using System.Runtime.InteropServices;

namespace DesktopOrganizer.Infrastructure.Win32;

/// <summary>
/// Native RECT struct (winuser.h). Coordinates are inclusive top-left, exclusive bottom-right.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// Native POINT struct (windef.h).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;
}

/// <summary>
/// Native WINDOWPLACEMENT struct (winuser.h). Used by GetWindowPlacement / SetWindowPlacement.
/// The <see cref="length"/> field MUST be initialized to <c>sizeof(WINDOWPLACEMENT)</c> by the caller.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public uint length;
    public uint flags;
    public uint showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

/// <summary>
/// Native MONITORINFOEX struct (winuser.h). Extends MONITORINFO with the device name.
/// The <see cref="cbSize"/> field MUST be initialized to <c>Marshal.SizeOf&lt;MONITORINFOEX&gt;()</c>
/// before calling GetMonitorInfo.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    public string szDevice;
}
