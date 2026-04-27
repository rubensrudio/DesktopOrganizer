namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Abstração sobre o gerenciador de virtual desktops do Windows.
/// Implementações: COM (Win10/11) ou Stub (fallback noop).
/// </summary>
public interface IVirtualDesktopManager
{
    /// <summary>
    /// Retorna o Guid do virtual desktop em que a janela se encontra,
    /// ou null se a informação não estiver disponível.
    /// </summary>
    Guid? GetWindowDesktopId(IntPtr hwnd);

    /// <summary>
    /// Move a janela para o virtual desktop indicado. Retorna false se a
    /// operação não puder ser realizada (sem lançar exceção).
    /// </summary>
    bool MoveWindowToDesktop(IntPtr hwnd, Guid desktopId);
}
