namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Exibe notificações do tray (balloon tips) ao usuário.
/// </summary>
public interface ITrayNotificationService
{
    void ShowSuccess(string message);
    void ShowResult(int restored, int failed);
}
