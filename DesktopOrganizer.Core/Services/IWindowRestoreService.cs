using DesktopOrganizer.Core.Domain;

namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Serviço responsável por restaurar uma janela individual a partir de um
/// <see cref="WindowEntry"/> previamente capturado.
/// </summary>
public interface IWindowRestoreService
{
    Task<RestoreResult> RestoreWindowAsync(WindowEntry entry, CancellationToken ct);
}
