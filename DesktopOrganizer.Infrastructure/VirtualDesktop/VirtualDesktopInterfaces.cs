using System.Runtime.InteropServices;

namespace DesktopOrganizer.Infrastructure.VirtualDesktop;

/// <summary>
/// Constantes COM para o <c>IVirtualDesktopManager</c> público do Windows Shell.
/// </summary>
/// <remarks>
/// <para>
/// O <c>IVirtualDesktopManager</c> público é parte estável do shell do Windows
/// desde a versão 1607 (Windows 10 Anniversary Update). Diferente das
/// interfaces COM internas do Explorer (<c>IVirtualDesktopManagerInternal</c>,
/// <c>IApplicationViewCollection</c>, etc.), esta API é documentada, mantida
/// pela Microsoft e não muda de IID a cada release.
/// </para>
/// <para>
/// CLSID e IID conforme documentação oficial:
/// <list type="bullet">
///   <item><description>CLSID_VirtualDesktopManager: <c>aa509086-5ca9-4c25-8f95-589d3c07b48a</c></description></item>
///   <item><description>IID_IVirtualDesktopManager: <c>a5cd92ff-29be-454c-8d04-d82879fb3f1b</c></description></item>
/// </list>
/// </para>
/// </remarks>
internal static class VirtualDesktopComConstants
{
    /// <summary>CLSID do <c>VirtualDesktopManager</c> exposto pelo shell do Windows.</summary>
    internal const string VirtualDesktopManagerClsid = "aa509086-5ca9-4c25-8f95-589d3c07b48a";

    /// <summary>IID da interface pública <c>IVirtualDesktopManager</c>.</summary>
    internal const string VirtualDesktopManagerIid = "a5cd92ff-29be-454c-8d04-d82879fb3f1b";
}

/// <summary>
/// Wrapper P/Invoke da interface COM pública <c>IVirtualDesktopManager</c>.
/// </summary>
/// <remarks>
/// <para>
/// A ordem dos métodos nesta declaração precisa coincidir <b>exatamente</b>
/// com a ordem da v-table COM nativa (após <c>IUnknown</c>). A Microsoft
/// define a ordem como: <c>IsWindowOnCurrentVirtualDesktop</c>,
/// <c>GetWindowDesktopId</c>, <c>MoveWindowToDesktop</c>.
/// </para>
/// <para>
/// Os métodos retornam <c>HRESULT</c> via <c>PreserveSig = true</c> para
/// permitir tratamento defensivo: códigos de erro nunca convertem para
/// exceção .NET; o caller decide como reagir (ver
/// <see cref="VirtualDesktopManagerImpl"/>).
/// </para>
/// </remarks>
[ComImport]
[Guid(VirtualDesktopComConstants.VirtualDesktopManagerIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IVirtualDesktopManagerCom
{
    /// <summary>
    /// Indica se a janela está no virtual desktop atualmente ativo.
    /// </summary>
    /// <param name="topLevelWindow">Handle da janela de nível superior.</param>
    /// <param name="onCurrentDesktop">Saída: <c>true</c> se a janela está no desktop atual.</param>
    /// <returns>HRESULT — <c>S_OK</c> em sucesso; código de erro caso contrário.</returns>
    [PreserveSig]
    int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);

    /// <summary>
    /// Obtém o GUID do virtual desktop que contém a janela.
    /// </summary>
    /// <param name="topLevelWindow">Handle da janela de nível superior.</param>
    /// <param name="desktopId">Saída: identificador do desktop.</param>
    /// <returns>HRESULT — <c>S_OK</c> em sucesso; código de erro caso contrário.</returns>
    [PreserveSig]
    int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);

    /// <summary>
    /// Move a janela para o virtual desktop indicado.
    /// </summary>
    /// <param name="topLevelWindow">Handle da janela de nível superior.</param>
    /// <param name="desktopId">GUID do desktop alvo.</param>
    /// <returns>HRESULT — <c>S_OK</c> em sucesso; código de erro caso contrário.</returns>
    [PreserveSig]
    int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
}
