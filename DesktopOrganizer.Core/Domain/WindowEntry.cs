namespace DesktopOrganizer.Core.Domain;

public enum WindowState
{
    Normal = 0,
    Maximized = 1,
    Minimized = 2
}

public class WindowEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public WindowState WindowState { get; set; } = WindowState.Normal;
    public string MonitorDeviceName { get; set; } = string.Empty;
    public Guid? VirtualDesktopId { get; set; }
}
