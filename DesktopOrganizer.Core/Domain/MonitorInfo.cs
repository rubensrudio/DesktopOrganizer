namespace DesktopOrganizer.Core.Domain;

/// <summary>
/// Retângulo de tela em coordenadas inteiras. Mantido interno ao Core para
/// evitar dependência de System.Drawing (que não está disponível em todos os
/// targets) e para que o domínio permaneça puro.
/// </summary>
public readonly record struct ScreenRectangle(int X, int Y, int Width, int Height);

/// <summary>
/// Informações de um monitor físico/lógico do sistema.
/// </summary>
public sealed class MonitorInfo
{
    public string DeviceName { get; init; } = string.Empty;
    public ScreenRectangle Bounds { get; init; }
}
