using System;

namespace DesktopOrganizer.App.TrayIcon;

/// <summary>
/// Stub temporário do controlador do tray icon. Implementação real é
/// responsabilidade da TASK-019 (NotifyIcon, menus dinâmicos, ações).
///
/// Esta classe existe APENAS para destravar o bootstrap (TASK-018): o
/// <see cref="App"/> precisa instanciar e disposar um TrayIconController
/// no ciclo de vida da aplicação. A TASK-019 substitui o conteúdo destes
/// métodos sem mudar a assinatura pública.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private bool _disposed;

    public TrayIconController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// Inicialização do tray. Stub no-op até a TASK-019.
    /// </summary>
    public void Initialize()
    {
        // TASK-019: criar NotifyIcon, registrar menus e handlers.
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // TASK-019: dispor NotifyIcon e recursos associados.
    }
}
