using DesktopOrganizer.Core.Domain;

namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Serviço responsável por capturar todas as janelas relevantes da sessão atual.
/// Implementação reside em Infrastructure (Win32WindowCaptureService).
/// </summary>
public interface IWindowCaptureService
{
    Task<IReadOnlyList<WindowEntry>> CaptureAllWindowsAsync();
}
