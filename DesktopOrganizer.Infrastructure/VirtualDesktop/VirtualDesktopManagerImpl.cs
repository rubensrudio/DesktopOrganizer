using System.Runtime.InteropServices;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.VirtualDesktop;

/// <summary>
/// Implementação de <see cref="IVirtualDesktopManager"/> baseada na interface
/// COM pública <c>IVirtualDesktopManager</c> do shell do Windows.
/// </summary>
/// <remarks>
/// <para>
/// Esta implementação cobre Windows 10 (1607+) e Windows 11. Usa apenas a
/// API <b>pública</b> e estável da Microsoft — não depende das interfaces
/// internas do Explorer, que mudam a cada release e exigiriam manutenção
/// constante.
/// </para>
/// <para>
/// Limitação conhecida: o <c>IVirtualDesktopManager</c> público só permite
/// mover janelas para desktops já existentes pelo seu GUID, e
/// <c>MoveWindowToDesktop</c> pode falhar com <c>E_ACCESSDENIED</c> em
/// janelas que pertencem a outro processo (UAC elevado). Esses casos são
/// tratados defensivamente: a falha é absorvida, o método retorna
/// <c>false</c> e o caller decide como prosseguir.
/// </para>
/// <para>
/// Estratégia de robustez (TASK-010 é de risco alto):
/// <list type="bullet">
///   <item><description>Inicialização COM dentro de try/catch — qualquer falha leva a <see cref="IsAvailable"/> = <c>false</c>.</description></item>
///   <item><description>Bootstrap (TASK-018) inspeciona <see cref="IsAvailable"/> e registra o <c>VirtualDesktopStub</c> quando esta implementação não está utilizável.</description></item>
///   <item><description>Métodos públicos <b>nunca</b> propagam exceção: HRESULTs de erro convertem para <c>null</c>/<c>false</c>.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class VirtualDesktopManagerImpl : IVirtualDesktopManager
{
    private const int HrSuccess = 0; // S_OK

    private readonly IVirtualDesktopManagerCom? _comInstance;

    /// <summary>
    /// Cria a instância e tenta inicializar o objeto COM subjacente.
    /// </summary>
    /// <remarks>
    /// Se a inicialização falhar por qualquer motivo (sistema sem suporte,
    /// CLSID indisponível, falha de marshaling, etc.), o construtor não
    /// lança: <see cref="IsAvailable"/> fica <c>false</c> e os métodos
    /// passam a comportar-se como no-ops, permitindo ao bootstrap optar
    /// pelo <c>VirtualDesktopStub</c> sem que o app aborte.
    /// </remarks>
    public VirtualDesktopManagerImpl()
    {
        try
        {
            Type? comType = Type.GetTypeFromCLSID(new Guid(VirtualDesktopComConstants.VirtualDesktopManagerClsid));
            if (comType is null)
            {
                _comInstance = null;
                return;
            }

            object? rawInstance = Activator.CreateInstance(comType);
            _comInstance = rawInstance as IVirtualDesktopManagerCom;
        }
        catch (COMException)
        {
            _comInstance = null;
        }
        catch (InvalidCastException)
        {
            _comInstance = null;
        }
        catch (PlatformNotSupportedException)
        {
            // PlatformNotSupportedException herda de NotSupportedException; capturar
            // a derivada deixa o intent (sistema sem suporte) explícito.
            _comInstance = null;
        }
        catch (NotSupportedException)
        {
            _comInstance = null;
        }
    }

    /// <summary>
    /// Indica se a inicialização COM teve sucesso e a implementação pode ser usada.
    /// </summary>
    /// <remarks>
    /// O bootstrap (TASK-018) deve consultar este flag para decidir entre
    /// registrar esta implementação ou cair no <see cref="VirtualDesktopStub"/>.
    /// </remarks>
    public bool IsAvailable => _comInstance is not null;

    /// <inheritdoc />
    /// <returns>
    /// O <see cref="Guid"/> do virtual desktop que contém a janela; <c>null</c>
    /// se a implementação não estiver disponível, o handle for inválido, ou
    /// qualquer falha COM ocorrer.
    /// </returns>
    public Guid? GetWindowDesktopId(IntPtr hwnd)
    {
        if (_comInstance is null)
        {
            return null;
        }

        try
        {
            int hr = _comInstance.GetWindowDesktopId(hwnd, out Guid desktopId);
            if (hr != HrSuccess)
            {
                return null;
            }

            return desktopId;
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    /// <returns>
    /// <c>true</c> se a janela foi efetivamente movida; <c>false</c> caso
    /// contrário (implementação indisponível, desktop inexistente, processo
    /// alvo elevado/inacessível, ou qualquer falha COM).
    /// </returns>
    public bool MoveWindowToDesktop(IntPtr hwnd, Guid desktopId)
    {
        if (_comInstance is null)
        {
            return false;
        }

        try
        {
            int hr = _comInstance.MoveWindowToDesktop(hwnd, ref desktopId);
            return hr == HrSuccess;
        }
        catch (COMException)
        {
            return false;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }
}
