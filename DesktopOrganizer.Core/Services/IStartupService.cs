namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Controla a inicialização automática da aplicação com o Windows.
/// Implementação concreta usa o Registry (HKCU\...\Run).
/// </summary>
public interface IStartupService
{
    void Enable(string executablePath);
    void Disable();
    bool IsEnabled();
}
