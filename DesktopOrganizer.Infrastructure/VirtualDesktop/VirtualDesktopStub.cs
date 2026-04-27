using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.VirtualDesktop;

/// <summary>
/// Implementação fallback (no-op) de <see cref="IVirtualDesktopManager"/>.
/// </summary>
/// <remarks>
/// <para>
/// Este stub é registrado no container de DI quando a inicialização do
/// <c>VirtualDesktopManagerImpl</c> baseado em COM falha (por exemplo, em
/// versões do Windows incompatíveis com as interfaces COM internas usadas
/// pelo Explorer, ou quando uma atualização do sistema quebra os IIDs/CLSIDs
/// esperados).
/// </para>
/// <para>
/// O stub nunca lança exceções: <see cref="GetWindowDesktopId"/> sempre
/// retorna <c>null</c> e <see cref="MoveWindowToDesktop"/> sempre retorna
/// <c>false</c>. Os use cases que dependem de virtual desktops (captura e
/// restauração) devem tratar esses retornos como "informação indisponível"
/// e seguir o fluxo principal sem abortar — o resultado prático é que a
/// posição/tamanho/monitor das janelas continuam sendo restaurados, apenas
/// o desktop virtual de destino é ignorado.
/// </para>
/// <para>
/// A implementação real via COM é entregue na TASK-010
/// (<c>VirtualDesktopManagerImpl</c>).
/// </para>
/// </remarks>
public sealed class VirtualDesktopStub : IVirtualDesktopManager
{
    /// <inheritdoc />
    /// <returns>Sempre <c>null</c>: a informação de virtual desktop é indisponível neste fallback.</returns>
    public Guid? GetWindowDesktopId(IntPtr hwnd) => null;

    /// <inheritdoc />
    /// <returns>Sempre <c>false</c>: o stub não move janelas entre desktops virtuais.</returns>
    public bool MoveWindowToDesktop(IntPtr hwnd, Guid desktopId) => false;
}
