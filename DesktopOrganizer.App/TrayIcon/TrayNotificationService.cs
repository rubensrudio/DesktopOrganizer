using System;
using DesktopOrganizer.Core.Services;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace DesktopOrganizer.App.TrayIcon;

/// <summary>
/// Implementação real de <see cref="ITrayNotificationService"/> (TASK-021)
/// que exibe balloon notifications via <c>H.NotifyIcon.Wpf</c>.
///
/// Modelo de bind tardio: o <see cref="TaskbarIcon"/> é criado pelo
/// <see cref="TrayIconController"/> em <c>Initialize()</c>, mas o DI
/// container precisa registrar este serviço como singleton ANTES do
/// controller existir (use cases dependem de <see cref="ITrayNotificationService"/>).
/// Para resolver a dependência circular sem inverter a propriedade do
/// <see cref="TaskbarIcon"/>, oferecemos <see cref="Bind"/>: o controller
/// chama-o logo após criar o ícone. Antes do bind, todas as chamadas
/// caem em no-op silencioso — comportamento intencional, pois durante o
/// bootstrap não há tray onde mostrar a notificação.
///
/// Thread-safety: <see cref="Bind"/> e os métodos públicos podem ser
/// chamados de threads diferentes (use cases rodam em background). A
/// referência ao ícone é volatile-equivalente via <c>lock</c> mínimo —
/// na prática, <c>Bind</c> ocorre uma única vez no startup, então o
/// custo é desprezível.
/// </summary>
internal sealed class TrayNotificationService : ITrayNotificationService
{
    private readonly object _gate = new();
    private TaskbarIcon? _icon;

    /// <summary>
    /// Vincula o <see cref="TaskbarIcon"/> ao serviço. Deve ser chamado
    /// pelo <see cref="TrayIconController"/> uma única vez, logo após
    /// criar o ícone em <c>Initialize()</c>.
    /// </summary>
    public void Bind(TaskbarIcon icon)
    {
        if (icon is null)
        {
            throw new ArgumentNullException(nameof(icon));
        }

        lock (_gate)
        {
            _icon = icon;
        }
    }

    /// <summary>
    /// Libera a referência ao ícone (chamado no Dispose do controller
    /// para evitar tentativas de notificar após o tray ter sido destruído).
    /// </summary>
    public void Unbind()
    {
        lock (_gate)
        {
            _icon = null;
        }
    }

    public void ShowSuccess(string message)
    {
        TryShow(title: "DesktopOrganizer", message: message ?? string.Empty, NotificationIcon.Info);
    }

    public void ShowResult(int restored, int failed)
    {
        var msg = $"Restauração concluída: {restored} janelas restauradas, {failed} falhas";
        TryShow(title: "DesktopOrganizer", message: msg, NotificationIcon.Info);
    }

    private void TryShow(string title, string message, NotificationIcon icon)
    {
        TaskbarIcon? local;
        lock (_gate)
        {
            local = _icon;
        }

        if (local is null)
        {
            // Sem bind: silencioso. O bootstrap pode invocar notificações
            // antes do tray existir; preferimos descartar a virar exceção.
            return;
        }

        try
        {
            local.ShowNotification(title: title, message: message, icon: icon);
        }
        catch
        {
            // Notificações são best-effort: nunca propagam para o caller.
        }
    }
}
