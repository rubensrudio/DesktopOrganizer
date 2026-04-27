using System.Runtime.InteropServices;
using System.Text;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Win32;

/// <summary>
/// Implementação de <see cref="IWindowCaptureService"/> que enumera todas as janelas
/// visíveis de nível superior usando APIs Win32 (EnumWindows + companions) e converte
/// cada janela em um <see cref="WindowEntry"/>.
///
/// Estratégia de enumeração:
/// 1. <c>EnumWindows</c> dispara um callback para cada janela top-level. Filtramos
///    por <c>IsWindowVisible</c> e acumulamos os HWNDs em uma lista local.
/// 2. Após a enumeração, processamos cada HWND chamando as APIs de geometria,
///    estado, processo e monitor — fora do callback para evitar reentrância.
/// 3. Toda a lógica é síncrona (Win32 APIs são síncronas); envolvemos em
///    <see cref="Task.Run"/> para honrar a assinatura assíncrona da interface
///    sem bloquear o chamador.
///
/// Resiliência: janelas pertencentes a processos protegidos do sistema (cuja
/// imagem não pode ser consultada via <c>QueryFullProcessImageName</c>) são
/// silenciosamente puladas — DORG-01 AC6 exige que a captura não aborte por
/// causa de uma janela individual.
/// </summary>
internal sealed class Win32WindowCaptureService : IWindowCaptureService
{
    private readonly IVirtualDesktopManager _virtualDesktopManager;

    public Win32WindowCaptureService(IVirtualDesktopManager virtualDesktopManager)
    {
        _virtualDesktopManager = virtualDesktopManager
            ?? throw new ArgumentNullException(nameof(virtualDesktopManager));
    }

    public Task<IReadOnlyList<WindowEntry>> CaptureAllWindowsAsync()
    {
        return Task.Run<IReadOnlyList<WindowEntry>>(() =>
        {
            // Etapa 1: enumerar HWNDs visíveis. O callback acumula em uma lista
            // local capturada via closure — EnumWindows é síncrono então não há
            // risco de race condition.
            var visibleHandles = new List<IntPtr>();

            bool Callback(IntPtr hWnd, IntPtr lParam)
            {
                if (Win32Interop.IsWindowVisible(hWnd))
                {
                    visibleHandles.Add(hWnd);
                }
                return true; // continua enumeração
            }

            Win32Interop.EnumWindows(Callback, IntPtr.Zero);

            // Etapa 2: para cada HWND, montar o WindowEntry. Falhas individuais
            // são absorvidas para não derrubar a captura inteira.
            var entries = new List<WindowEntry>(visibleHandles.Count);

            foreach (var hwnd in visibleHandles)
            {
                var entry = TryBuildEntry(hwnd);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        });
    }

    /// <summary>
    /// Constrói um <see cref="WindowEntry"/> a partir de um HWND. Retorna null
    /// quando informações essenciais (executável, geometria) não puderem ser
    /// obtidas — caso típico de janelas de processos do sistema operacional.
    /// </summary>
    private WindowEntry? TryBuildEntry(IntPtr hwnd)
    {
        try
        {
            // ---- Geometria (posição/tamanho) ----
            if (!Win32Interop.GetWindowRect(hwnd, out var rect))
            {
                return null;
            }

            // ---- Estado da janela (normal/min/max) ----
            var placement = new WINDOWPLACEMENT
            {
                length = (uint)Marshal.SizeOf<WINDOWPLACEMENT>()
            };

            var windowState = WindowState.Normal;
            if (Win32Interop.GetWindowPlacement(hwnd, ref placement))
            {
                windowState = MapShowCmdToWindowState(placement.showCmd);
            }

            // ---- PID + caminho do executável ----
            // Se o caminho não puder ser obtido (processo protegido), pulamos
            // a janela inteira: sem executável não é possível restaurá-la.
            Win32Interop.GetWindowThreadProcessId(hwnd, out var pid);

            var executablePath = TryGetExecutablePath(pid);
            if (string.IsNullOrEmpty(executablePath))
            {
                return null;
            }

            // ---- Título da janela ----
            var windowTitle = GetWindowTitle(hwnd);

            // ---- Monitor ----
            var monitorDeviceName = GetMonitorDeviceName(hwnd);

            // ---- Virtual Desktop ----
            // Nunca lança: a abstração já trata erros internamente.
            var virtualDesktopId = _virtualDesktopManager.GetWindowDesktopId(hwnd);

            return new WindowEntry
            {
                ProcessName = Path.GetFileNameWithoutExtension(executablePath),
                ExecutablePath = executablePath,
                WindowTitle = windowTitle,
                X = rect.Left,
                Y = rect.Top,
                Width = rect.Width,
                Height = rect.Height,
                WindowState = windowState,
                MonitorDeviceName = monitorDeviceName,
                VirtualDesktopId = virtualDesktopId
            };
        }
        catch
        {
            // Defesa em profundidade: qualquer exceção inesperada de uma única
            // janela não deve derrubar a captura completa (DORG-01 AC6).
            return null;
        }
    }

    /// <summary>
    /// Mapeia o <c>showCmd</c> do WINDOWPLACEMENT para o enum de domínio.
    /// SW_SHOWMINIMIZED (2) → Minimized, SW_SHOWMAXIMIZED (3) → Maximized,
    /// qualquer outro valor (1, 4, 5, ...) → Normal.
    /// </summary>
    private static WindowState MapShowCmdToWindowState(uint showCmd)
    {
        return showCmd switch
        {
            (uint)Win32Interop.SW_SHOWMINIMIZED => WindowState.Minimized,
            (uint)Win32Interop.SW_SHOWMAXIMIZED => WindowState.Maximized,
            _ => WindowState.Normal
        };
    }

    /// <summary>
    /// Obtém o caminho completo do executável associado ao PID via
    /// <c>OpenProcess</c> (com PROCESS_QUERY_LIMITED_INFORMATION, que funciona
    /// inclusive para a maior parte dos processos elevados sem exigir UAC) +
    /// <c>QueryFullProcessImageName</c>. Retorna string vazia em caso de falha
    /// (processos do sistema operacional bloqueiam consultas).
    /// </summary>
    private static string TryGetExecutablePath(uint pid)
    {
        if (pid == 0)
        {
            return string.Empty;
        }

        var hProcess = Win32Interop.OpenProcess(
            Win32Interop.PROCESS_QUERY_LIMITED_INFORMATION,
            false,
            pid);

        if (hProcess == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            const int bufferCapacity = 1024;
            var buffer = new StringBuilder(bufferCapacity);
            uint size = bufferCapacity;

            if (!Win32Interop.QueryFullProcessImageName(hProcess, 0, buffer, ref size))
            {
                return string.Empty;
            }

            return buffer.ToString(0, (int)size);
        }
        catch
        {
            return string.Empty;
        }
        finally
        {
            Win32Interop.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Lê o título da janela via GetWindowTextLength + GetWindowText.
    /// Retorna string vazia se a janela não tiver título ou se as APIs falharem.
    /// </summary>
    private static string GetWindowTitle(IntPtr hwnd)
    {
        var length = Win32Interop.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        // +1 para o terminador nulo que GetWindowText espera no buffer.
        var buffer = new StringBuilder(length + 1);
        var copied = Win32Interop.GetWindowText(hwnd, buffer, length + 1);
        return copied > 0 ? buffer.ToString() : string.Empty;
    }

    /// <summary>
    /// Identifica o monitor mais próximo da janela e retorna seu nome de device
    /// (ex.: <c>\\.\DISPLAY1</c>). Retorna string vazia em caso de falha.
    /// </summary>
    private static string GetMonitorDeviceName(IntPtr hwnd)
    {
        var hMonitor = Win32Interop.MonitorFromWindow(hwnd, Win32Interop.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero)
        {
            return string.Empty;
        }

        var info = new MONITORINFOEX
        {
            cbSize = Marshal.SizeOf<MONITORINFOEX>(),
            szDevice = string.Empty
        };

        return Win32Interop.GetMonitorInfo(hMonitor, ref info)
            ? info.szDevice ?? string.Empty
            : string.Empty;
    }
}
